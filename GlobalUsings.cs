// Project-wide global usings.
//
// The Validation namespace is exposed globally so that the move of
// ValidationIssue / ValidationSeverity / ValidationCategory out of
// ShiftManager.Services (their original home) into ShiftManager.Models.Validation
// (their canonical home as part of the project-wide error-handling overhaul)
// remains source-compatible: existing unqualified references in the Services
// namespace continue to resolve without per-file using directives.
global using ShiftManager.Models.Validation;
