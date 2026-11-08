# ReSharper inspection review — 2026-11-08

Reviewed all 513 entries in the supplied ReSharper 2026.2.4.1 report (ToolsVersion 262.0.20261030.161637). Counts include linked-source duplicates. The repository targets .NET 10; the installed SDK is 10.0.403.

Shared solution settings suppress optional syntax rewrites, namespace/folder matching (the portable assemblies intentionally retain WinMon namespaces), async-void WPF event-handler suggestions, lock-type migration, and partial enum switches with intentional defaults. Defect checks remain enabled; exceptions have source-scoped explanations. Windows/API spelling is added to the shared dictionary.

Changes remove disposable-object captures from setup startup, correct nullable annotations and smoke assertions, complete parameter documentation, remove unused imports and a dead label helper, and make default grid cells explicit. No sampling algorithm, persisted format or visible behavior is changed. The unused setup registration result is discarded while preserving its validation call. Early pipe/service-handle disposal remains required for rollback and SCM deletion.

| Inspection | Report entries | Disposition |
|---|---:|---|
| `AccessToDisposedClosure` | 3 | Removed disposable captures from setup startup; synchronous callback suppressed locally |
| `AccessToStaticMemberViaDerivedType` | 9 | Fixed |
| `ArgumentsStyleOther` | 3 | Shared settings: retain current syntax/API conventions |
| `ArrangeMissingParentheses` | 7 | Shared settings: retain current syntax/API conventions |
| `ArrangeObjectCreationWhenTypeEvident` | 15 | Shared settings: retain current syntax/API conventions |
| `ArrangeObjectCreationWhenTypeNotEvident` | 41 | Shared settings: retain current syntax/API conventions |
| `ArrangeRedundantParentheses` | 11 | Shared settings: retain current syntax/API conventions |
| `ArrangeStaticMemberQualifier` | 4 | Shared settings: retain current syntax/API conventions |
| `AssignNullToNotNullAttribute` | 2 | Fixed |
| `AsyncVoidEventHandlerMethod` | 5 | Shared settings: retain current syntax/API conventions |
| `BadControlBracesIndent` | 1 | Fixed |
| `CanReplaceCastWithVariableType` | 2 | Shared settings: retain current syntax/API conventions |
| `CanSimplifyDictionaryLookupWithTryAdd` | 1 | Shared settings: retain current syntax/API conventions |
| `ChangeFieldTypeToSystemThreadingLock` | 3 | Shared settings: retain current syntax/API conventions |
| `CheckNamespace` | 53 | Shared settings: assembly separation intentionally retains WinMon namespaces |
| `ClassNeverInstantiated.Local` | 1 | Source-scoped suppression with reason |
| `CommentTypo` | 13 | Shared dictionary: exact domain/API spelling |
| `CompareOfFloatsByEqualityOperator` | 12 | Source-scoped suppression with reason |
| `ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract` | 7 | Corrected nullable annotations; retained payload validation with scoped suppression |
| `ConditionalAccessQualifierIsNonNullableAccordingToAPIContract` | 5 | Removed redundant token access; retained initialization/persistence/ownership checks locally |
| `ConvertConditionalTernaryExpressionToSwitchExpression` | 1 | Shared settings: retain current syntax/API conventions |
| `ConvertIfStatementToReturnStatement` | 12 | Shared settings: retain current syntax/API conventions |
| `ConvertIfStatementToSwitchStatement` | 7 | Shared settings: retain current syntax/API conventions |
| `ConvertToAutoPropertyWhenPossible` | 13 | Shared settings: retain current syntax/API conventions |
| `ConvertToAutoPropertyWithPrivateSetter` | 3 | Shared settings: retain current syntax/API conventions |
| `ConvertToConstant.Local` | 1 | Shared settings: retain current syntax/API conventions |
| `ConvertToPrimaryConstructor` | 6 | Shared settings: retain current syntax/API conventions |
| `DisposeOnUsingVariable` | 2 | Source-scoped suppression with reason |
| `EmptyGeneralCatchClause` | 1 | Source-scoped suppression with reason |
| `ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator` | 1 | Shared settings: retain current syntax/API conventions |
| `ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator` | 2 | Shared settings: retain current syntax/API conventions |
| `GrammarMistakeInComment` | 5 | Reworded comments without changing their meaning |
| `IdentifierTypo` | 9 | Shared dictionary: exact domain/API spelling |
| `InlineTemporaryVariable` | 2 | Shared settings: retain current syntax/API conventions |
| `InvalidXmlDocComment` | 7 | Fixed |
| `InvertIf` | 21 | Shared settings: retain current syntax/API conventions |
| `InvokeAsExtensionMember` | 7 | Shared settings: retain current syntax/API conventions |
| `LocalizableElement` | 2 | Source-scoped suppression with reason |
| `LoopCanBeConvertedToQuery` | 14 | Shared settings: retain current syntax/API conventions |
| `MemberCanBeMadeStatic.Local` | 2 | Shared settings: retain current syntax/API conventions |
| `MemberHidesStaticFromOuterClass` | 3 | Source-scoped suppression with reason |
| `MergeIntoLogicalPattern` | 2 | Shared settings: retain current syntax/API conventions |
| `MergeIntoPattern` | 12 | Shared settings: retain current syntax/API conventions |
| `MethodHasAsyncOverload` | 16 | Source-scoped suppression with reason |
| `MethodSupportsCancellation` | 5 | Source-scoped suppression with reason |
| `MoveLocalFunctionAfterJumpStatement` | 12 | Shared settings: retain current syntax/API conventions |
| `NotAccessedPositionalProperty.Local` | 2 | Source-scoped suppression with reason |
| `NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract` | 1 | Fixed |
| `ParameterOnlyUsedForPreconditionCheck.Global` | 2 | Source-scoped suppression with reason |
| `PartialTypeWithSinglePart` | 2 | Fixed |
| `PossibleMultipleEnumeration` | 5 | Source-scoped suppression with reason |
| `PreferConcreteValueOverDefault` | 1 | Shared settings: retain current syntax/API conventions |
| `PreferExplicitlyProvidedTupleComponentName` | 4 | Shared settings: retain current syntax/API conventions |
| `RedundantAnonymousTypePropertyName` | 1 | Shared settings: retain current syntax/API conventions |
| `RedundantArgumentDefaultValue` | 4 | Shared settings: retain current syntax/API conventions |
| `RedundantAssignment` | 1 | Fixed |
| `RedundantBoolCompare` | 1 | Shared settings: retain current syntax/API conventions |
| `RedundantCast` | 4 | Shared settings: retain current syntax/API conventions |
| `RedundantExtendsListEntry` | 4 | Shared settings: retain current syntax/API conventions |
| `RedundantJumpStatement` | 1 | Shared settings: retain current syntax/API conventions |
| `RedundantLambdaParameterType` | 2 | Shared settings: retain current syntax/API conventions |
| `RedundantNameQualifier` | 2 | Shared settings: retain current syntax/API conventions |
| `RedundantTypeDeclarationBody` | 2 | Shared settings: retain current syntax/API conventions |
| `RedundantUsingDirective` | 11 | Fixed |
| `ReplaceConditionalExpressionWithNullCoalescing` | 2 | Shared settings: retain current syntax/API conventions |
| `ReturnTypeCanBeNotNullable` | 1 | Fixed |
| `SeparateLocalFunctionsWithJumpStatement` | 5 | Shared settings: retain current syntax/API conventions |
| `StringLiteralTypo` | 18 | Shared dictionary: exact domain/API spelling |
| `SwitchStatementHandlesSomeKnownEnumValuesWithDefault` | 3 | Shared settings: retain current syntax/API conventions |
| `UnusedMember.Global` | 18 | Deleted unused label helper; retained linked API, public contracts and named XAML parts locally |
| `UnusedVariable` | 1 | Fixed |
| `UseAwaitUsing` | 2 | Shared settings: retain current syntax/API conventions |
| `UseCollectionExpression` | 33 | Shared settings: retain current syntax/API conventions |
| `UseDeconstruction` | 1 | Shared settings: retain current syntax/API conventions |
| `UseNullPropagation` | 2 | Shared settings: retain current syntax/API conventions |
| `UseObjectOrCollectionInitializer` | 1 | Shared settings: retain current syntax/API conventions |
| `UseUtf8StringLiteral` | 1 | Shared settings: retain current syntax/API conventions |
| `UseVerbatimString` | 2 | Shared settings: retain current syntax/API conventions |
| `UseWithExpressionToCopyRecord` | 2 | Shared settings: retain current syntax/API conventions |
| `UseWithExpressionToCopyStruct` | 1 | Shared settings: retain current syntax/API conventions |
| `Xaml.MissingGridIndex` | 3 | Fixed |
| `Xaml.RedundantStyledValue` | 1 | Shared settings: retain current syntax/API conventions |

Verification:

- `./tools/Verify.ps1 -Smoke connections,services`: passed; both builds and all five test suites passed. All 43 smoke PNGs were inspected, including narrow and translated layouts.
- `./tools/Verify.ps1 -Store`: both builds and four test suites passed, but the setup suite intermittently failed in `RevocationTransaction_PreservesRecoveryBackupWhenRollbackFails` and, on a full rerun, `RevocationTransaction_RollsBackAfterRestartFailureAndStopsCachedServiceFirst`. The latter reported `File.Replace`: "Unable to remove the file to be replaced." An isolated snapshot of unchanged HEAD `44d09e6` reproduced the same rollback failure on the fourth repeated run of its four revocation tests. The failure predates this cleanup; its filesystem cause remains undetermined. A subsequent full-suite run passed both configurations; these earlier failures remain intermittent. The rollback code/tests were not changed or suppressed.
- Installed InspectCode 2026.2.3.1 completed successfully with hint-level reporting over `src/**`. None of the 82 inspection types in the supplied report remain. It also reports 188 diagnostics outside that report, primarily .NET analyzer/native-import suggestions and three XAML ambiguous-reference diagnostics; those are outside this cleanup. The exact IDE version 2026.2.4.1 was not rerun.
- Live elevated setup/UAC installation and explicit hardware/elevation test cases were not exercised. No performance benchmark was needed for this inspection cleanup: sampling, ranking, locking and enumeration algorithms were retained.

Full-suite follow-up: standard and Store verification both passed, including solution/benchmark builds and all six test projects. NetworkProbe tests were run separately because Verify.ps1 omits that project. Standard: 1,289 passed; Store: 1,291 passed; zero failures. The three explicitly opt-in tests (fixture generation, elevated ETW, elevated ACL provisioning) were not selected. Logs are in artifacts/inspection-review/full-suite/.
