# PreToolUse hook: blocks native Edit/Write/MultiEdit on files whose extension is in $blocked,
# forcing .cs work through Serena's Roslyn-aware tools (symbol-level edits, LSP diagnostics).
#
# Rebuilt from scratch (2026-07-17) after the original .claude/ harness config was accidentally
# deleted mid-session (git clean over-broad path list). Behavior reconstructed from CLAUDE.md's
# description: "blocks native Edit/Write/MultiEdit on .cs ... flip the hook's $blocked list if you
# later want Serena to own the frontend too." The TypeScript/React frontend is intentionally left
# to native tools, so only .cs is blocked by default.

$blocked = @('.cs')

$inputJson = [Console]::In.ReadToEnd()
$hookInput = $inputJson | ConvertFrom-Json

$toolName = $hookInput.tool_name
if ($toolName -notin @('Edit', 'Write', 'MultiEdit')) {
    exit 0
}

$filePath = $hookInput.tool_input.file_path
if (-not $filePath) {
    exit 0
}

$ext = [System.IO.Path]::GetExtension($filePath)
if ($ext -notin $blocked) {
    exit 0
}

$reason = "Native $toolName is blocked for '$ext' files in this project. " +
    "Use Serena's tools instead (find_symbol / replace_symbol_body / insert_after_symbol / " +
    "insert_before_symbol / replace_content for edits, create_text_file to create or overwrite) " +
    "so changes stay symbol-aware and get LSP diagnostics. See CLAUDE.md's Tooling section."

$output = @{
    hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision        = 'deny'
        permissionDecisionReason  = $reason
    }
} | ConvertTo-Json -Depth 5

Write-Output $output
exit 0
