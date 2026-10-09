// tools/ci/style-check.cs — the repository's text conventions (.editorconfig), checked in CI (tracker S57):
//   dotnet run tools/ci/style-check.cs [--fix]
// LF line endings, a final newline, no trailing whitespace (Markdown may keep it), and C# indented with tabs (spaces
// may follow for alignment). `dotnet format whitespace` is not the gate: on tab-indented code with aligned
// continuation lines it moves comments and continuations to wrong columns. --fix repairs everything except indentation.
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

var fix = args.Contains("--fix");
var root = Run("git", "rev-parse --show-toplevel").Trim();
var files = (Run("git", "ls-files") + Run("git", "ls-files --others --exclude-standard"))
	.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
	.Distinct()
	.Where(f => Regex.IsMatch(f, @"\.(cs|csproj|props|targets|qml|js|sh|py|json|xaml|slnx|yml|yaml|md|in)$"))
	.Where(f => !f.Contains("/Resources/Seed/", StringComparison.Ordinal))   // app data, kept byte for byte
	.Order(StringComparer.Ordinal)
	.ToList();

var problems = new List<string>();
var fixedFiles = 0;
foreach (var file in files)
{
	var path = Path.Combine(root, file);
	if (!File.Exists(path))
		continue;
	var text = File.ReadAllText(path);
	if (text.Length == 0)
		continue;
	var issues = new List<string>();
	var result = text;
	if (result.Contains('\r'))
	{
		issues.Add("CRLF line endings");
		result = result.Replace("\r\n", "\n").Replace('\r', '\n');
	}
	if (!file.EndsWith(".md", StringComparison.Ordinal))
	{
		var lines = result.Split('\n');
		var trailing = lines.Select((l, i) => (l, i)).Where(x => x.l.Length > 0 && char.IsWhiteSpace(x.l[^1])).Select(x => x.i + 1).ToList();
		if (trailing.Count > 0)
		{
			issues.Add($"trailing whitespace on line{(trailing.Count > 1 ? "s" : "")} {string.Join(", ", trailing.Take(5))}{(trailing.Count > 5 ? ", …" : "")}");
			result = string.Join('\n', lines.Select(l => l.TrimEnd(' ', '\t')));
		}
	}
	if (!result.EndsWith('\n'))
	{
		issues.Add("no newline at the end of the file");
		result += "\n";
	}
	if (file.EndsWith(".cs", StringComparison.Ordinal))
	{
		var spaced = result.Split('\n').Select((l, i) => (l, i)).Where(x => Regex.IsMatch(x.l, @"^ {2,}\S")).Select(x => x.i + 1).ToList();
		if (spaced.Count > 0)
			problems.Add($"{file}: indented with spaces, not tabs (line{(spaced.Count > 1 ? "s" : "")} {string.Join(", ", spaced.Take(5))}{(spaced.Count > 5 ? ", …" : "")})");
	}
	if (issues.Count == 0)
		continue;
	if (fix)
	{
		File.WriteAllText(path, result, new UTF8Encoding(text.StartsWith('﻿')));
		fixedFiles++;
	}
	else
	{
		problems.Add($"{file}: {string.Join("; ", issues)}");
	}
}

foreach (var problem in problems)
	Console.Error.WriteLine(problem);
Console.WriteLine(fix
	? $"style: {files.Count} files, {fixedFiles} fixed, {problems.Count} to fix by hand"
	: $"style: {files.Count} files, {problems.Count} with problems{(problems.Count > 0 ? " (dotnet run tools/ci/style-check.cs --fix repairs all but indentation)" : "")}");
return problems.Count == 0 ? 0 : 1;

string Run(string program, string arguments)
{
	var info = new ProcessStartInfo(program, arguments) { RedirectStandardOutput = true, UseShellExecute = false };
	using var process = Process.Start(info)!;
	var output = process.StandardOutput.ReadToEnd();
	process.WaitForExit();
	return output;
}
