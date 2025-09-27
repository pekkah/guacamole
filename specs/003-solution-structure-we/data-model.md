# Data Model: Initial Solution Structure

**Date**: 2025-09-27
**Feature**: Initial Solution Structure

## Entity Definitions

### Project
**Description**: Represents a .NET project within the solution
**Attributes**:
- Name: string (e.g., "Guacamole", "Guacamole.SourceGenerators")
- Type: ProjectType (Library, SourceGenerator, Test)
- TargetFramework: string ("net10.0")
- IsPackable: boolean
- ProjectPath: string (relative path from solution root)
- Dependencies: List<PackageReference>

**Validation Rules**:
- Name must follow .NET naming conventions (PascalCase, no spaces)
- ProjectPath must be unique within solution
- Test projects must have IsPackable = false
- All projects must target same framework version

### PackageReference
**Description**: NuGet package dependency
**Attributes**:
- PackageId: string
- Version: string
- IncludeAssets: string[] (optional)
- PrivateAssets: string[] (optional)

**Validation Rules**:
- PackageId must be valid NuGet package identifier
- Version must be valid semantic version or range

### BuildConfiguration
**Description**: Shared build configuration via Directory.Build.props
**Attributes**:
- TargetFramework: string
- LangVersion: string
- NullableEnabled: boolean
- TreatWarningsAsErrors: boolean
- Authors: string
- Company: string
- Product: string
- PackageLicenseExpression: string

**Validation Rules**:
- Single configuration shared across all projects
- TargetFramework must be consistent

### Solution
**Description**: The top-level container for all projects
**Attributes**:
- Name: string ("Guacamole")
- Projects: List<Project>
- BuildConfiguration: BuildConfiguration
- GitVersionConfig: GitVersionConfiguration

**State Transitions**: N/A (structural entity only)

### GitVersionConfiguration
**Description**: Version management configuration
**Attributes**:
- Workflow: string ("GitHubFlow/v1")
- NextVersion: string ("1.0.0")
- Mode: string ("ContinuousDelivery")
- BranchConfigurations: Dictionary<string, BranchConfig>

**Validation Rules**:
- Must follow GitVersion schema
- NextVersion must be valid semantic version

## Relationships

```
Solution (1) -----> (*) Project
    |
    +---> (1) BuildConfiguration
    |
    +---> (1) GitVersionConfiguration

Project (*) -----> (*) PackageReference
```

## Constraints

1. **Naming Convention**: All project names must follow pattern `Guacamole[.Component][.Tests]`
2. **Folder Structure**: Projects must reside in src/ or tests/ directories
3. **Version Synchronization**: All packable projects share same version number
4. **Test Isolation**: Test projects cannot reference each other
5. **Dependency Direction**: Source generators cannot depend on core library at runtime