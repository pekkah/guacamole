# Guacamole Framework

[![CI](https://github.com/pekkah/guacamole/actions/workflows/ci.yml/badge.svg)](https://github.com/pekkah/guacamole/actions/workflows/ci.yml)
[![Publish](https://github.com/pekkah/guacamole/actions/workflows/publish.yml/badge.svg)](https://github.com/pekkah/guacamole/actions/workflows/publish.yml)
[![NuGet](https://img.shields.io/nuget/v/Guacamole.Core.svg)](https://www.nuget.org/packages/Guacamole.Core/)

Foundation framework for incremental compilers, powering the Fugu language compiler and similar tooling.

## Projects

- **Guacamole**: Core incremental query engine library
- **Guacamole.SourceGenerators**: Source generators for code generation

## Requirements

- .NET 10 RC1 or later

## Building

```bash
dotnet build
```

## Testing

```bash
dotnet test
```