using System;

namespace FileWatcherContext;

public record TrackedFile(string Path, DateTime LastModified, long Size);
