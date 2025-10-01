# Feature Specification: ComputingContextBase

**Feature Branch**: `007-computingcontextbase-we-need`
**Created**: 2025-09-30
**Status**: Draft
**Input**: User description: "ComputingContextBase - we need a base class for users to inherit similar to EF context. User should be able to add properties to this class as types of IncrementalValueProvider<T> or IncrementalValuesProvider<T>  (src/Guacamole.Core/Providers/IncrementalValueProvider.cs). System should use source generator to generate any boilerplate code to hook these properties to the context. System should also generate methods for Add, Remove, Replace the types of T given for the properties. Batch updates should be also possible where updates trigger when batch is committed.  Changes should automatically trigger updates on incremental value providers. This is the core part on how user will integrate their code with our framework."

## Execution Flow (main)
```
1. Parse user description from Input
   → If empty: ERROR "No feature description provided"
2. Extract key concepts from description
   → Identify: actors, actions, data, constraints
3. For each unclear aspect:
   → Mark with [NEEDS CLARIFICATION: specific question]
4. Fill User Scenarios & Testing section
   → If no clear user flow: ERROR "Cannot determine user scenarios"
5. Generate Functional Requirements
   → Each requirement must be testable
   → Mark ambiguous requirements
6. Identify Key Entities (if data involved)
7. Run Review Checklist
   → If any [NEEDS CLARIFICATION]: WARN "Spec has uncertainties"
   → If implementation details found: ERROR "Remove tech details"
8. Return: SUCCESS (spec ready for planning)
```

---

## ⚡ Quick Guidelines
- ✅ Focus on WHAT users need and WHY
- ❌ Avoid HOW to implement (no tech stack, APIs, code structure)
- 👥 Written for business stakeholders, not developers

### Section Requirements
- **Mandatory sections**: Must be completed for every feature
- **Optional sections**: Include only when relevant to the feature
- When a section doesn't apply, remove it entirely (don't leave as "N/A")

### For AI Generation
When creating this spec from a user prompt:
1. **Mark all ambiguities**: Use [NEEDS CLARIFICATION: specific question] for any assumption you'd need to make
2. **Don't guess**: If the prompt doesn't specify something (e.g., "login system" without auth method), mark it
3. **Think like a tester**: Every vague requirement should fail the "testable and unambiguous" checklist item
4. **Common underspecified areas**:
   - User types and permissions
   - Data retention/deletion policies
   - Performance targets and scale
   - Error handling behaviors
   - Integration requirements
   - Security/compliance needs

---

## Clarifications

### Session 2025-09-30
- Q: When a batch operation fails partway through (e.g., an Add operation throws an exception on the 3rd item out of 5), how should the system handle the partial changes? → A: Fail fast with proper error diagnostics
- Q: When a user tries to Add an item that already exists in the collection, what should the system do? → A: Throw an exception and fail the operation
- Q: How should the system handle Remove operations on items that don't exist in the collection? → A: Return a boolean indicating success/failure
- Q: The spec mentions thread-safety but doesn't specify the concurrency model. What level of concurrent access should be supported? → A: Multiple readers, single writer with locks
- Q: What are the target performance characteristics for context operations? → A: None, set later
- Q: How should the system distinguish between input sources (that need Add/Remove/Replace methods) and derived computations (that are read-only transformations)? → A: Generate methods only for properties with setters, derived ones are get-only

---

## User Scenarios & Testing *(mandatory)*

### Primary User Story
As a framework user (developer), I want to create a class that inherits from ComputingContextBase where I can define properties as incremental value providers, so that I can manage my application's data state with automatic change propagation and batch update capabilities, similar to how Entity Framework provides a DbContext for database operations.

### Acceptance Scenarios
1. **Given** a user creates a class inheriting from ComputingContextBase with IncrementalValueProvider<T> properties that have setters, **When** they instantiate the context, **Then** the system automatically generates methods for Add, Remove, and Replace operations for input source properties only
2. **Given** a user has a ComputingContext with defined properties, **When** they perform Add/Remove/Replace operations, **Then** changes are tracked and dependent incremental value providers are automatically updated
3. **Given** a user starts a batch operation, **When** they perform multiple Add/Remove/Replace operations within the batch, **Then** changes are collected and incremental value providers are updated only when the batch is committed
4. **Given** a user modifies data through the context, **When** changes occur, **Then** all dependent incremental value providers automatically reflect the changes without manual intervention
5. **Given** a user defines both IncrementalValueProvider<T> and IncrementalValuesProvider<T> properties, **When** they use the context, **Then** both single-value and multi-value scenarios work correctly with generated operations

### Edge Cases
- Add operations throw exceptions when attempting to add duplicate items
- Remove operations return false when attempting to remove non-existent items
- Batch operations fail fast with detailed error diagnostics when exceptions occur during processing
- Circular dependencies in incremental value providers are detected at build-time by the source generator and produce compilation errors with clear diagnostic messages
- Properties with conflicting type parameters (e.g., same property name with different generic arguments in inheritance chain) produce compilation errors during source generation

## Requirements *(mandatory)*

### Functional Requirements
- **FR-001**: System MUST provide a base class (ComputingContextBase) that users can inherit from to create their application contexts
- **FR-002**: System MUST allow users to declare properties of type IncrementalValueProvider<T> and IncrementalValuesProvider<T> on their context classes
- **FR-003**: System MUST automatically generate Add, Remove, and Replace methods only for properties with setters (input sources), where Add operations throw exceptions for duplicate items and Remove operations return boolean success indicators
- **FR-004**: System MUST support batch operations where multiple changes can be collected and committed together, with fail-fast behavior that stops immediately on errors and provides detailed error diagnostics
- **FR-005**: System MUST automatically trigger updates on incremental value providers when data changes occur
- **FR-006**: System MUST integrate with the existing IncrementalValueProvider and IncrementalValuesProvider infrastructure
- **FR-007**: Users MUST be able to commit batched changes to trigger incremental value provider updates
- **FR-008**: System MUST track dependencies between incremental value providers to ensure proper update ordering
- **FR-009**: System MUST provide change tracking capabilities to identify what has been added, removed, or replaced
- **FR-010**: System MUST ensure thread-safety for concurrent access to context operations using a multiple-readers, single-writer locking model
- **FR-011**: System performance requirements will be defined during implementation phase based on usage patterns

### Key Entities *(include if feature involves data)*
- **ComputingContextBase**: Base class that users inherit from, provides the foundation for property declarations and change tracking
- **Input Source Properties**: User-defined properties with setters of type IncrementalValueProvider<T> or IncrementalValuesProvider<T> that represent mutable data sources
- **Derived Computation Properties**: User-defined get-only properties that represent read-only transformations of input sources
- **Generated Operations**: Auto-generated Add/Remove/Replace methods for each type T found in context properties
- **Batch Context**: Mechanism for collecting multiple operations before committing changes to trigger updates
- **Change Tracking**: Source-generated property-specific change lists that monitor modifications and coordinate incremental value provider updates with zero boxing overhead

---

## Review & Acceptance Checklist
*GATE: Automated checks run during main() execution*

### Content Quality
- [ ] No implementation details (languages, frameworks, APIs)
- [ ] Focused on user value and business needs
- [ ] Written for non-technical stakeholders
- [ ] All mandatory sections completed

### Requirement Completeness
- [ ] No [NEEDS CLARIFICATION] markers remain
- [ ] Requirements are testable and unambiguous
- [ ] Success criteria are measurable
- [ ] Scope is clearly bounded
- [ ] Dependencies and assumptions identified

---

## Execution Status
*Updated by main() during processing*

- [x] User description parsed
- [x] Key concepts extracted
- [x] Ambiguities marked
- [x] User scenarios defined
- [x] Requirements generated
- [x] Entities identified
- [ ] Review checklist passed

---