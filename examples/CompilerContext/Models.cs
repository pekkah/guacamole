using System;

namespace CompilerContext;

public record Project(string Name, string Version);

public record SourceFile(string Path, string Content);
