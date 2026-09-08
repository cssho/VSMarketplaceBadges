#!/usr/bin/env node
// PostToolUse hook: run `dotnet format whitespace` on any .cs file Claude writes or edits.
// Reads the hook payload from stdin. Exits 0 always -- formatting must never block an edit.
"use strict";

const { execFileSync } = require("child_process");
const path = require("path");

let raw = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", (chunk) => (raw += chunk));
process.stdin.on("end", () => {
  try {
    const payload = JSON.parse(raw || "{}");
    const file =
      (payload.tool_response && payload.tool_response.filePath) ||
      (payload.tool_input && payload.tool_input.file_path);
    if (!file || path.extname(file).toLowerCase() !== ".cs") return;

    const repo = path.resolve(__dirname, "..", "..");
    // `dotnet format --include` only matches repo-relative paths; an absolute
    // Windows path silently formats nothing.
    const rel = path.relative(repo, path.resolve(file)).split(path.sep).join("/");
    if (!rel || rel.startsWith("..")) return;

    execFileSync(
      "dotnet",
      ["format", "whitespace", "VSMarketplaceBadges.csproj", "--include", rel],
      { cwd: repo, stdio: "ignore" }
    );
  } catch {
    // Formatting is best-effort: a missing SDK or an unparsable payload is not a failure.
  }
});
