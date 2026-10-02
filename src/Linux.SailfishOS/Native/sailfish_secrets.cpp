/*
 * SecureStorage bridge: MAUI ISecureStorage on the Sailfish Secrets daemon.
 *
 * A separate library (libsailfishsecretsbridge.so) because Sailfish Secrets is not preinstalled,
 * so the host shim must load without it; managed code dlopen()s this and falls back to a file store.
 *
 * One owner-only, device-lock collection per app (readable after first unlock, never prompts),
 * stored with the sqlcipher plugin. A device-lock collection opens only once the daemon has the
 * device lock code, which needs Jolla's device-lock integration plugin; without it (and the system
 * password agent refuses third-party apps) the collection stays locked, so sfsec_open probes it and
 * reports a locked collection instead of letting every request fail. Secret names are "k" + 30 hex of SHA-256(key), because the
 * plugin only takes short alphanumeric names.
 *
 * Qt thread only (QtDBus needs the host's QCoreApplication); requests block in waitForFinished().
 */
#include <QtCore/QByteArray>
#include <QtCore/QCoreApplication>
#include <QtCore/QCryptographicHash>
#include <QtCore/QString>

#include <Secrets/createcollectionrequest.h>
#include <Secrets/deletecollectionrequest.h>
#include <Secrets/deletesecretrequest.h>
#include <Secrets/result.h>
#include <Secrets/secret.h>
#include <Secrets/secretmanager.h>
#include <Secrets/storedsecretrequest.h>
#include <Secrets/storesecretrequest.h>

#include <cstdlib>
#include <cstring>

using namespace Sailfish::Secrets;

#define SFSEC_API extern "C" __attribute__((visibility("default")))

/* Return codes: 0 ok; 1 not found (get/delete); < 0 bridge errors; > 1 a
 * Sailfish::Secrets::Result::ErrorCode (+ 1000, to keep 1 unambiguous). */
enum {
    SFSEC_OK = 0,
    SFSEC_NOT_FOUND = 1,
    SFSEC_E_NOAPP = -1,       // no QCoreApplication (host not started)
    SFSEC_E_DAEMON = -2,      // manager failed to connect to sailfishsecretsd
    SFSEC_E_NOTOPEN = -3,     // sfsec_open not called / failed
    SFSEC_E_ARGS = -4,
};

namespace {

SecretManager *g_manager = nullptr;
QString g_collection;
bool g_collectionReady = false;
QByteArray g_lastError;

int fail(const Result &r, const char *what)
{
    g_lastError = QByteArray(what) + ": " + QByteArray::number(int(r.errorCode())) + " "
                  + r.errorMessage().toUtf8();
    return 1000 + int(r.errorCode());
}

QString secretName(const char *key)
{
    const QByteArray digest = QCryptographicHash::hash(QByteArray(key), QCryptographicHash::Sha256);
    return QStringLiteral("k") + QString::fromLatin1(digest.toHex().left(30));
}

Secret::Identifier identifier(const char *key)
{
    return Secret::Identifier(secretName(key), g_collection, SecretManager::DefaultEncryptedStoragePluginName);
}

bool isLocked(const Result &r)
{
    return r.errorCode() == Result::CollectionIsLockedError
           || r.errorCode() == Result::SecretsPluginIsLockedError
           || r.errorCode() == Result::IncorrectAuthenticationCodeError
           || r.errorCode() == Result::OperationRequiresUserInteraction;
}

bool isMissing(const Result &r)
{
    return r.errorCode() == Result::InvalidSecretError
           || r.errorCode() == Result::InvalidSecretIdentifierError
           || r.errorCode() == Result::InvalidCollectionError;
}

int ensureCollection()
{
    if (g_collectionReady)
        return SFSEC_OK;
    CreateCollectionRequest req;
    req.setManager(g_manager);
    req.setCollectionName(g_collection);
    req.setCollectionLockType(CreateCollectionRequest::DeviceLock);
    req.setDeviceLockUnlockSemantic(SecretManager::DeviceLockKeepUnlocked);
    req.setAccessControlMode(SecretManager::OwnerOnlyMode);
    req.setUserInteractionMode(SecretManager::PreventInteraction);
    req.setStoragePluginName(SecretManager::DefaultEncryptedStoragePluginName);
    req.setEncryptionPluginName(SecretManager::DefaultEncryptedStoragePluginName);
    req.setAuthenticationPluginName(SecretManager::DefaultAuthenticationPluginName);
    req.startRequest();
    req.waitForFinished();
    const Result r = req.result();
    if (r.code() != Result::Succeeded && r.errorCode() != Result::CollectionAlreadyExistsError)
        return fail(r, "create collection");
    g_collectionReady = true;
    return SFSEC_OK;
}

bool store(const char *key, const char *data, int len, Result *result)
{
    Secret secret(identifier(key));
    secret.setData(QByteArray(data, len));
    secret.setType(Secret::TypeBlob);
    StoreSecretRequest req;
    req.setManager(g_manager);
    req.setSecretStorageType(StoreSecretRequest::CollectionSecret);
    req.setUserInteractionMode(SecretManager::PreventInteraction);
    req.setSecret(secret);
    req.startRequest();
    req.waitForFinished();
    *result = req.result();
    return result->code() == Result::Succeeded;
}

/* An existing secret: SecretAlreadyExistsError per the API, but the sqlcipher
 * plugin of 0.2.44 answers DatabaseQueryError ("UNIQUE constraint failed"). */
bool mayAlreadyExist(const Result &r)
{
    return r.errorCode() == Result::SecretAlreadyExistsError
           || r.errorCode() == Result::DatabaseQueryError
           || r.errorCode() == Result::DatabaseTransactionError
           || r.errorCode() == Result::DatabaseError;
}

} // namespace

/* Connects to the daemon and prepares the app's collection. Qt thread. */
SFSEC_API int sfsec_open(const char *collection)
{
    if (!collection || !*collection)
        return SFSEC_E_ARGS;
    if (!QCoreApplication::instance()) {
        g_lastError = "no QCoreApplication (the Qt host is not running)";
        return SFSEC_E_NOAPP;
    }
    if (!g_manager)
        g_manager = new SecretManager(QCoreApplication::instance());
    if (!g_manager->isInitialized()) {
        g_lastError = "SecretManager not initialized (sailfishsecretsd unreachable — daemon not installed, "
                      "or the sandbox lacks the Secrets permission)";
        return SFSEC_E_DAEMON;
    }
    g_collection = QString::fromUtf8(collection);
    g_collectionReady = false;
    const int rc = ensureCollection();
    if (rc != SFSEC_OK)
        return rc;
    // An existing collection answers CollectionAlreadyExists even while locked: read a key to know it is usable.
    StoredSecretRequest probe;
    probe.setManager(g_manager);
    probe.setIdentifier(identifier("maui-sailfish-probe"));
    probe.setUserInteractionMode(SecretManager::PreventInteraction);
    probe.startRequest();
    probe.waitForFinished();
    const Result r = probe.result();
    if (r.code() != Result::Succeeded && !isMissing(r))
        return fail(r, isLocked(r) ? "collection locked (the daemon has no device lock code)" : "probe");
    return SFSEC_OK;
}

// Runs a request for one key without user interaction and waits for it (the bridge API is synchronous).
template <typename Request>
static Result runForKey(Request &req, const char *key)
{
    req.setManager(g_manager);
    req.setIdentifier(identifier(key));
    req.setUserInteractionMode(SecretManager::PreventInteraction);
    req.startRequest();
    req.waitForFinished();
    return req.result();
}

SFSEC_API int sfsec_set(const char *key, const char *data, int len)
{
    if (!key || (!data && len > 0) || len < 0)
        return SFSEC_E_ARGS;
    if (!g_manager || g_collection.isEmpty())
        return SFSEC_E_NOTOPEN;
    int rc = ensureCollection();
    if (rc != SFSEC_OK)
        return rc;
    const char *bytes = data ? data : "";
    Result first;
    if (store(key, bytes, len, &first))
        return SFSEC_OK;
    if (!mayAlreadyExist(first))
        return fail(first, "store");
    // Overwrite = delete + store (the daemon never replaces in place). A
    // delete that finds nothing means the first error was a real one.
    DeleteSecretRequest del;
    const Result delResult = runForKey(del, key);
    if (delResult.code() != Result::Succeeded)
        return fail(first, "store");
    Result second;
    if (store(key, bytes, len, &second))
        return SFSEC_OK;
    return fail(second, "replace");
}

/* *out is malloc()ed (free with sfsec_free); returns 1 when the key is absent. */
SFSEC_API int sfsec_get(const char *key, char **out, int *len)
{
    if (!key || !out || !len)
        return SFSEC_E_ARGS;
    *out = nullptr;
    *len = 0;
    if (!g_manager || g_collection.isEmpty())
        return SFSEC_E_NOTOPEN;
    StoredSecretRequest req;
    const Result reqResult = runForKey(req, key);
    if (reqResult.code() != Result::Succeeded)
        return isMissing(reqResult) ? SFSEC_NOT_FOUND : fail(reqResult, "get");
    const QByteArray data = req.secret().data();
    *out = static_cast<char *>(std::malloc(size_t(data.size()) + 1));
    if (!*out)
        return SFSEC_E_ARGS;
    std::memcpy(*out, data.constData(), size_t(data.size()));
    (*out)[data.size()] = '\0';
    *len = data.size();
    return SFSEC_OK;
}

SFSEC_API int sfsec_delete(const char *key)
{
    if (!key)
        return SFSEC_E_ARGS;
    if (!g_manager || g_collection.isEmpty())
        return SFSEC_E_NOTOPEN;
    // The daemon deletes a missing secret "successfully"; ISecureStorage.Remove
    // must answer false then — probe first.
    StoredSecretRequest probe;
    const Result probeResult = runForKey(probe, key);
    if (probeResult.code() != Result::Succeeded)
        return isMissing(probeResult) ? SFSEC_NOT_FOUND : fail(probeResult, "delete (probe)");
    DeleteSecretRequest req;
    const Result reqResult = runForKey(req, key);
    if (reqResult.code() != Result::Succeeded)
        return isMissing(reqResult) ? SFSEC_NOT_FOUND : fail(reqResult, "delete");
    return SFSEC_OK;
}

/* RemoveAll: drops the whole collection (recreated on the next set). */
SFSEC_API int sfsec_clear(void)
{
    if (!g_manager || g_collection.isEmpty())
        return SFSEC_E_NOTOPEN;
    DeleteCollectionRequest req;
    req.setManager(g_manager);
    req.setCollectionName(g_collection);
    req.setStoragePluginName(SecretManager::DefaultEncryptedStoragePluginName);
    req.setUserInteractionMode(SecretManager::PreventInteraction);
    req.startRequest();
    req.waitForFinished();
    const Result r = req.result();
    g_collectionReady = false;
    if (r.code() != Result::Succeeded && !isMissing(r))
        return fail(r, "clear");
    return SFSEC_OK;
}

SFSEC_API void sfsec_free(char *p)
{
    std::free(p);
}

SFSEC_API const char *sfsec_last_error(void)
{
    return g_lastError.constData();
}
