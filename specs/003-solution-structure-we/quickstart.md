# Quickstart: Initial Solution Structure

**Feature**: Initial Solution Structure
**Date**: 2025-09-27

## Prerequisites

- .NET 10 SDK RC1 (10.0.0-rc.1.25451.107 or later)
- Visual Studio 2022 17.14.13 Preview 1.0 OR VS Code with C# Dev Kit
- Git installed and configured
- PowerShell Core (for cross-platform scripts)

## Setup Steps

### 1. Clone and Initialize

```bash
# Clone the repository
git clone https://github.com/pekkah/guacamole.git
cd guacamole

# Checkout the feature branch
git checkout 003-solution-structure-we
```

### 2. Verify Solution Structure

```bash
# Verify the folder structure exists
ls -la src/
ls -la tests/
ls -la *.props *.yml *.sln

# Expected output:
# src/
#   Guacamole/
#   Guacamole.SourceGenerators/
# tests/
#   Guacamole.Tests/
#   Guacamole.SourceGenerators.Tests/
# Root files:
#   Directory.Build.props
#   GitVersion.yml
#   Guacamole.sln
```

### 3. Build the Solution

```bash
# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Expected: Build succeeded with 0 errors
```

### 4. Run Tests

```bash
# Run all unit tests
dotnet test tests/Guacamole.Tests/

# Run snapshot tests
dotnet test tests/Guacamole.SourceGenerators.Tests/

# Expected: All tests pass
```

### 5. Verify Package Creation

```bash
# Create NuGet packages
dotnet pack src/Guacamole/ -c Release
dotnet pack src/Guacamole.SourceGenerators/ -c Release

# Verify packages were created
ls src/Guacamole/bin/Release/*.nupkg
ls src/Guacamole.SourceGenerators/bin/Release/*.nupkg

# Expected: Two .nupkg files with matching version numbers
```

### 6. Verify Version Synchronization

```bash
# Check version from GitVersion
dotnet tool install -g GitVersion.Tool
dotnet-gitversion

# Build and check assembly versions
dotnet build -c Release
dotnet list package --include-transitive

# Expected: All Guacamole packages show same version
```

### 7. Verify Test Project Configuration

```bash
# Attempt to pack test projects (should fail/skip)
dotnet pack tests/Guacamole.Tests/ -c Release

# Expected: No package created due to IsPackable=false
```

## Validation Checklist

- [ ] Solution file (Guacamole.sln) exists at root
- [ ] All 4 projects build successfully
- [ ] Directory.Build.props applies to all projects
- [ ] GitVersion calculates version correctly
- [ ] Core library package is created
- [ ] Source generators package is created
- [ ] Test projects are NOT packaged
- [ ] All projects target .NET 10.0
- [ ] Version numbers synchronized across packages
- [ ] Source Link information included in packages

## Troubleshooting

### Issue: .NET 10 RC1 not found

```bash
# Install .NET 10 RC1
dotnet --list-sdks
# If not present, download from https://dot.net/
```

### Issue: GitVersion not working

```bash
# Install GitVersion tool
dotnet tool install -g GitVersion.Tool

# Initialize git repository if needed
git init
git add .
git commit -m "Initial commit"
```

### Issue: Build errors with Visual Studio

- Ensure using VS2022 17.14.13 Preview 1.0 or later
- OR use VS Code with C# Dev Kit extension
- Verify .NET 10 SDK is installed

## Next Steps

After successful validation:

1. Review generated package contents
2. Verify symbol packages (.snupkg) are created
3. Test package installation in a sample project
4. Configure CI/CD pipeline for automated builds
5. Begin implementing core query engine functionality

## Success Criteria

✅ All projects build without errors
✅ Tests execute successfully
✅ NuGet packages created with correct metadata
✅ Version synchronization working via GitVersion
✅ Test projects excluded from packaging
✅ Solution follows specified folder structure
