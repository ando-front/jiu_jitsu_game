// Expose Runtime internals to the EditMode test assembly so that
// MonoBehaviour-side hooks (e.g. BJJInputProvider.SetSnapshotForTest) can be
// driven from NUnit without going through the Unity Input System backend.
// Per docs/design/stage2_game_manager_v1.md §6.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("BJJSimulator.Tests")]
