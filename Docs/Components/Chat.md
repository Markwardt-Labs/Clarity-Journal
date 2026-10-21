# Chat

`ChatService`, `ChatStore`, `JournalExport` and `ClaudeRunner` answer questions about the journal with Claude Code.

## One conversation, answered one question at a time

There is one chat. A question is stored and announced immediately, then answered in the background; questions asked meanwhile queue and are answered in order, because each answer resumes the same Claude Code session and two runs of one session must not overlap. `ChatWorkingChanged` is true from the first queued question until the last answer is stored. The answer is stored before it is announced, so a client that was offline reads it from `GetChat`.

## Session continuity

The first question starts a Claude Code session under a new id, kept in the stored meta values only once the run succeeded. Later questions resume it, so Claude Code remembers the conversation. Clearing the chat deletes the messages and the stored id. If a resume fails because Claude Code no longer has the session, the id is dropped so the next question starts a fresh session instead of failing forever.

## The files Claude reads

The journal is mirrored into the `export` folder of the data folder: `entries/<year>/<date>.md` for each entry and `summaries/...` for each summary. The folder is rebuilt from the database at every server start, and kept current on each save, delete and summary write, each file replaced atomically. The chat system prompt tells Claude Code this layout, to start from summaries for broad questions and search entries for specifics, to name the dates it relies on, and to say so when the journal does not answer.

## What Claude Code may do

A chat run is started in the export folder with only the Read, Grep and Glob tools available and pre-approved, in `dontAsk` permission mode, which denies anything that is not pre-approved instead of prompting. It cannot run commands or edit files. The server's configuration may add extra arguments, which is the place to choose a model and the one way to widen this, deliberately.

## Retention

A check deletes the messages older than `chatRetentionDays` (two by default): once at startup and then after every local midnight. It waits for an answer being written, announces the deletion with a `ChatTrimmed` event so open apps read the chat again, and when no message remains it forgets the Claude Code session as clearing does.

## Clearing

The app asks for confirmation before it asks the server to clear: a dialog over the chat says the whole conversation will be deleted, and only its Clear button sends the request. The clear button is unavailable while the chat is empty.

## Failures become messages

If Claude Code cannot be started, is not signed in or times out, the answer stored in the chat is a message saying so, so the problem is visible on every device and after a reconnect, without a separate error path.

## Running the command

`ClaudeRunner` starts `claude -p --output-format json` once per request, sends the prompt on standard input (so a long prompt never meets a command line limit), reads the JSON result and returns its `result` text. An `is_error` result, a non-zero exit without a usable result, an empty answer, a missing executable and a run past `claudeTimeoutMinutes` all become a `ClaudeException`. A cancelled or timed out run kills the process tree. MCP servers are not loaded, which keeps startup fast and the run self-contained.
