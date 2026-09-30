/*
 * F5: Harbour launcher for .NET MAUI apps on Sailfish OS.
 *
 * Harbour allows exactly one ELF executable, /usr/bin/<package>, which must
 * link against glibc's __libc_start_main and EXPORT main() — mapplauncherd's
 * booster (X-Nemo-Application-Type=silica-qt5) dlopen()s the binary into its
 * pre-initialised Qt process and calls main() there. Every other ELF object
 * must live in /usr/share/<package>/lib/. The .NET apphost does neither, so
 * this tiny host takes its place: it finds its own install location (dladdr —
 * correct both when exec'd and when loaded by the booster, where
 * /proc/self/exe is the booster), loads libhostfxr.so from
 * /usr/share/<package>/lib/ and runs <assembly>.dll there through the .NET
 * native hosting API (self-contained: hostfxr resolves hostpolicy/coreclr
 * next to the app).
 *
 * The assembly name is read from the single *.runtimeconfig.json in that
 * directory, so one prebuilt launcher serves every app.
 */
#define _GNU_SOURCE
#include <dirent.h>
#include <dlfcn.h>
#include <limits.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef void *hostfxr_handle;

struct hostfxr_initialize_parameters {
    size_t size;
    const char *host_path;
    const char *dotnet_root;
};

typedef int32_t (*hostfxr_initialize_for_dotnet_command_line_fn)(
    int argc, const char **argv,
    const struct hostfxr_initialize_parameters *parameters,
    hostfxr_handle *host_context_handle);
typedef int32_t (*hostfxr_run_app_fn)(const hostfxr_handle host_context_handle);
typedef int32_t (*hostfxr_close_fn)(const hostfxr_handle host_context_handle);

/* The package name: the basename of this binary (/usr/bin/<package>). */
static int package_name(char *out, size_t cap)
{
    Dl_info info;
    if (!dladdr((void *)&package_name, &info) || !info.dli_fname)
        return -1;
    const char *slash = strrchr(info.dli_fname, '/');
    const char *base = slash ? slash + 1 : info.dli_fname;
    if (!*base || strlen(base) >= cap)
        return -1;
    strcpy(out, base);
    return 0;
}

/* <dir>/<name>.runtimeconfig.json → <name> (the app assembly). */
static int assembly_name(const char *dir, char *out, size_t cap)
{
    static const char suffix[] = ".runtimeconfig.json";
    DIR *d = opendir(dir);
    if (!d)
        return -1;
    int found = -1;
    struct dirent *e;
    while ((e = readdir(d)) != NULL) {
        size_t n = strlen(e->d_name);
        size_t s = sizeof(suffix) - 1;
        if (n > s && strcmp(e->d_name + n - s, suffix) == 0 && n - s < cap) {
            memcpy(out, e->d_name, n - s);
            out[n - s] = '\0';
            found = 0;
            break;
        }
    }
    closedir(d);
    return found;
}

__attribute__((visibility("default")))
int main(int argc, char **argv)
{
    char pkg[256];
    char dir[PATH_MAX];
    char name[256];
    char path[PATH_MAX];

    if (package_name(pkg, sizeof pkg) != 0) {
        fprintf(stderr, "sailfish-launcher: cannot resolve the package name\n");
        return 127;
    }
    const char *override = getenv("SAILFISH_LAUNCHER_APPDIR");   /* tests */
    if (override && *override)
        snprintf(dir, sizeof dir, "%s", override);
    else
        snprintf(dir, sizeof dir, "/usr/share/%s/lib", pkg);
    if (assembly_name(dir, name, sizeof name) != 0) {
        fprintf(stderr, "sailfish-launcher: no *.runtimeconfig.json in %s\n", dir);
        return 127;
    }

    snprintf(path, sizeof path, "%s/libhostfxr.so", dir);
    void *fxr = dlopen(path, RTLD_NOW | RTLD_LOCAL);
    if (!fxr) {
        fprintf(stderr, "sailfish-launcher: %s\n", dlerror());
        return 127;
    }
    hostfxr_initialize_for_dotnet_command_line_fn init =
        (hostfxr_initialize_for_dotnet_command_line_fn)dlsym(fxr, "hostfxr_initialize_for_dotnet_command_line");
    hostfxr_run_app_fn run = (hostfxr_run_app_fn)dlsym(fxr, "hostfxr_run_app");
    hostfxr_close_fn close_fn = (hostfxr_close_fn)dlsym(fxr, "hostfxr_close");
    if (!init || !run || !close_fn) {
        fprintf(stderr, "sailfish-launcher: libhostfxr.so lacks the hosting API\n");
        return 127;
    }

    /* argv for hostfxr: <app.dll> followed by the app's own arguments. */
    char dll[PATH_MAX];
    snprintf(dll, sizeof dll, "%s/%s.dll", dir, name);
    const char **args = calloc((size_t)argc + 1, sizeof *args);
    if (!args)
        return 127;
    args[0] = dll;
    for (int i = 1; i < argc; ++i)
        args[i] = argv[i];

    char host[PATH_MAX];
    snprintf(host, sizeof host, "%s/%s", dir, name);   /* the "apphost" hostfxr expects */
    struct hostfxr_initialize_parameters params = { sizeof params, host, dir };
    hostfxr_handle ctx = NULL;
    int32_t rc = init(argc, args, &params, &ctx);
    if (rc != 0 || !ctx) {
        fprintf(stderr, "sailfish-launcher: hostfxr init failed (0x%x)\n", (unsigned)rc);
        free(args);
        return 127;
    }
    rc = run(ctx);
    close_fn(ctx);
    free(args);
    return (int)rc;
}
