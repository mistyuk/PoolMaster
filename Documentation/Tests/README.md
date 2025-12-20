# PoolMaster Unit Tests

This folder contains unit tests for PoolMaster's pure logic components. Tests are designed to run fast without Unity scene dependencies.

## Test Coverage

### ✅ PoolMetricsTests.cs (15 tests)
Tests for pool metrics calculations:
- Reuse efficiency (0% to 100%, including edge cases)
- Current active object count (including negative values)
- Creates per spawn ratio (with zero spawn guard)
- Average expansion intervals with NaN guards
- Metric merging logic
- Edge cases (zero spawns, time equality, created > spawned)

### ✅ CollectionPoolTests.cs (17 tests)
Tests for collection pooling:
- List<T> pooling and reuse
- HashSet<T> pooling and reuse
- Dictionary<K,V> pooling and reuse
- Type separation (no mixing)
- Clear behavior
- Null safety
- Pool count sanity checks

### ✅ PoolSnapshotTests.cs (8 tests)
Tests for snapshot aggregation:
- Total object calculations
- Global utilization percentages
- Single pool snapshots
- Null safety
- Timestamp capture (±15s tolerance for CI)

### ✅ PoolCommandBufferTests.cs (18 tests)
Tests for thread-safe command buffering:
- Enqueue operations (spawn, return, batch)
- Pending operation tracking
- Flush order (returns before spawns)
- Batch command handling
- Null safety
- Clear behavior

### ✅ PoolRequestTests.cs (11 tests)
Tests for pool configuration:
- Factory method validation
- Size constraint relationships (not exact values)
- Timing configuration
- Category assignment
- Null prefab handling
- Negative size clamping

## Running Tests

### Unity Test Runner
1. Open Unity Test Runner: `Window > General > Test Runner`
2. Switch to `EditMode` tab
3. Click `Run All` or select specific tests

### Command Line
```bash
# Run all tests
Unity.exe -runTests -testPlatform EditMode -testResults results.xml -projectPath .

# Run specific test
Unity.exe -runTests -testPlatform EditMode -editorTestsFilter "PoolMaster.Tests.CollectionPoolTests"
```

## Test Architecture

### Pure Logic Testing
Most tests focus on **pure data/math** with minimal Unity dependencies:
- ✅ No scene loading or prefab instantiation
- ✅ Minimal GameObject usage (only in PoolRequestTests and FakePool cleanup)
- ✅ Fast execution (< 1 second total)
- ✅ Deterministic results (no Time.time, explicit floats)

### Test Helpers
- `FakePool` - Mock IPoolControl for command buffer testing (spawns tracked GameObjects for cleanup)
- Direct constructor access via InternalsVisibleTo (no reflection needed)
- Automatic cleanup in `TearDown` methods

## Best Practices

### What IS Tested (Unit Tests)
- ✅ Calculations and formulas
- ✅ Data structure behavior
- ✅ Edge case handling
- ✅ Validation logic
- ✅ Command buffer ordering

### What is NOT Tested (Requires PlayMode)
- ❌ Scene management and hierarchy
- ❌ Component interactions (Transforms, Rigidbodies)
- ❌ Unity Update/LateUpdate loops
- ❌ Prefab instantiation from assets
- ❌ Real GameObject pooling lifecycle

Use Unity's **PlayMode tests** for integration testing with real prefabs and scenes.

## Adding New Tests

### Template
```csharp
using NUnit.Framework;

namespace PoolMaster.Tests
{
    public class YourFeatureTests
    {
        [SetUp]
        public void Setup()
        {
            // Initialize test state
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up
        }

        [Test]
        public void YourTest_Scenario_ExpectedResult()
        {
            // Arrange
            var input = ...;
            
            // Act
            var result = ...;
            
            // Assert
            Assert.AreEqual(expected, result);
        }
    }
}
```

### Guidelines
1. Name tests clearly: `Method_Scenario_ExpectedResult`
2. Use `[SetUp]` and `[TearDown]` for cleanup (especially for GameObjects)
3. Keep tests isolated (no shared state)
4. Test one thing per test method
5. Use explicit floats instead of Time.time for determinism
6. Test relationships (>=, >, <) over exact values when formulas might change
7. Focus on correctness invariants, not implementation details

## Test Statistics

| Category | Test Count | Status |
|----------|-----------|---------|
| **PoolMetrics** | 15 | ✅ Pass |
| **CollectionPool** | 17 | ✅ Pass |
| **PoolSnapshot** | 8 | ✅ Pass |
| **PoolCommandBuffer** | 18 | ✅ Pass |
| **PoolRequest** | 11 | ✅ Pass |
| **Total** | **69** | ✅ **All Pass** |

## CI/CD Integration

### GitHub Actions Example
```yaml
- name: Run Tests
  run: |
    unity-editor -runTests -testPlatform EditMode \
      -testResults results.xml \
      -projectPath $PROJECT_PATH
```

### Test Results
Tests produce standard NUnit XML format compatible with:
- Jenkins
- TeamCity
- Azure DevOps
- GitHub Actions
- GitLab CI

## Continuous Testing

### Watch Mode (Editor)
Tests can run automatically on code changes using Unity Test Runner's continuous mode.

### Pre-Commit Hook
Add to `.git/hooks/pre-commit`:
```bash
#!/bin/sh
unity-editor -runTests -testPlatform EditMode -batchmode -quit
```

## License

Same as PoolMaster (MIT License)
