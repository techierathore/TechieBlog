// Project-wide global usings for BlogEngine.
//
// Purpose: every file in this assembly touches Dapper and the shared model types, so importing
// them once here keeps the per-file using lists down to what is genuinely file-specific.
//
// Code Flow: the C# compiler applies these to every file in the project before compilation; there
// is no runtime component and nothing to call.
//
// Dependencies: BlogEngine.DaCore (the connection factory and generic repository),
// BlogModels (entities and interfaces), Dapper, System.Data.
//
// Usage: add a global using here only when a namespace is genuinely needed almost everywhere.
// A namespace imported globally stops being visible at the point of use, which makes an
// unfamiliar type harder to place - the cost is paid by every future reader of every file.

global using BlogEngine.DaCore;
global using BlogModels;
global using Dapper;
global using System.Data;
