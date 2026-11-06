using Dgm;
using DgmCustomCourses;
using UnityEngine;

// Executes the production isolation code against small component doubles.
// This verifies ownership/restoration policy, not Unity/IL2CPP callback wiring.
var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    assertions++;
}

var hidden = new Terrain { name = "base ground", Height = 30f };
var imported = new Terrain { name = "imported ground", Height = 4f };
var baseMapper = new SurfaceMapper { name = "base mapper", terrain = hidden };
var importedMapper = new SurfaceMapper { name = "imported mapper", terrain = imported };
var originallyDisabled = new SurfaceMapper { terrain = hidden, enabled = false };
var solver = new DiscGroundSolver();
var disc = new DiscBehaviour
{
    terrain = hidden,
    groundContact = new DiscGroundContact { mapper = baseMapper, solver = solver, onGround = true }
};
disc.transform.position = new Vector3(175.8f, 7.4f, 23f);
var originalPosition = disc.transform.position;

// Imported mode is a strict boundary: built-in throws are untouched.
CourseScenePatcher.IsImportedMapActive = false;
ImportedTerrainIsolation.RememberHiddenTerrain(hidden);
ImportedTerrainIsolation.DetachHiddenTerrain(disc);
Check(disc.terrain == hidden && disc.groundContact.solver == solver, "Built-in throw was changed");

CourseScenePatcher.IsImportedMapActive = true;
ImportedTerrainIsolation.SuspendMapper(importedMapper);
ImportedTerrainIsolation.SuspendMapper(baseMapper);
ImportedTerrainIsolation.SuspendMapper(baseMapper);
ImportedTerrainIsolation.SuspendMapper(originallyDisabled);
Check(!baseMapper.enabled && importedMapper.enabled, "Only the hidden source should be disabled");
Check(baseMapper.DisableCalls == 1, "Repeated setup should not trigger extra disable transitions");
Check(!SurfaceMapper.Active.Contains(baseMapper) && SurfaceMapper.Active.Contains(importedMapper), "The component lifecycle must unregister hidden terrain");

ImportedTerrainIsolation.DetachHiddenTerrain(disc);
Check(disc.terrain == null, "Hidden SampleHeight reference remains");
Check(disc.groundContact.mapper == null && disc.groundContact.solver == null, "Cached solver still owns the disc");
Check(!disc.groundContact.onGround && disc.groundContact.stepsOffGround == int.MaxValue, "Stale native contact remains");
Check(disc.transform.position.Equals(originalPosition), "Isolation must not reposition the disc");

// Native Throw() can reacquire a disabled terrain. Detaching again must work
// without losing the original snapshot used when leaving the imported round.
disc.terrain = hidden;
ImportedTerrainIsolation.DetachHiddenTerrain(disc);
Check(disc.terrain == null, "Reacquired hidden terrain was retained");

// A valid imported mapper/solver must survive the same cleanup hooks.
var validDisc = new DiscBehaviour
{
    terrain = imported,
    groundContact = new DiscGroundContact { mapper = importedMapper, solver = solver, onGround = true }
};
ImportedTerrainIsolation.DetachHiddenTerrain(validDisc);
Check(validDisc.terrain == imported && validDisc.groundContact.solver == solver && validDisc.groundContact.onGround, "Imported source was incorrectly suppressed");

// Do not restore an old cache over a newly selected, valid terrain.
var changedDisc = new DiscBehaviour { terrain = hidden };
ImportedTerrainIsolation.DetachHiddenTerrain(changedDisc);
changedDisc.terrain = imported;
ImportedTerrainIsolation.Restore();
Check(disc.terrain == hidden, "Original cached terrain was not restored");
Check(changedDisc.terrain == imported, "Restoration overwrote a newer terrain");
Check(baseMapper.enabled && !originallyDisabled.enabled, "Original enabled states were not restored");
Check(SurfaceMapper.Active.Contains(baseMapper), "Lifecycle restoration failed to register native source");
Check(!ImportedTerrainIsolation.IsHidden(hidden), "Hidden ownership leaked after cleanup");
ImportedTerrainIsolation.Restore();
Check(baseMapper.EnableCalls == 1, "Repeated cleanup should be harmless");

// A later imported round gets a fresh ownership set, independent of map names,
// world heights and coordinate ranges. The former base is not globally disabled.
var laterTerrain = new Terrain { Height = 80f };
var laterMapper = new SurfaceMapper { terrain = laterTerrain };
ImportedTerrainIsolation.RememberHiddenTerrain(laterTerrain);
ImportedTerrainIsolation.SuspendMapper(laterMapper);
ImportedTerrainIsolation.SuspendMapper(baseMapper);
Check(!laterMapper.enabled && baseMapper.enabled, "Terrain ownership leaked across rounds");
ImportedTerrainIsolation.Restore();
Check(laterMapper.enabled, "Later round failed to restore");

Console.WriteLine($"PASS: {assertions} imported terrain isolation assertions (component doubles; no game started).");

namespace UnityEngine
{
    public class Object
    {
        private static int nextId;
        private readonly int id = ++nextId;
        public string name = "test";
        public int GetInstanceID() => id;
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public class Transform { public Vector3 position; }
    public class Terrain : Object
    {
        public float Height;
        public float SampleHeight(Vector3 _) => Height;
    }
}
namespace Dgm
{
    public class SurfaceMapper : UnityEngine.Object
    {
        public static readonly HashSet<SurfaceMapper> Active = new();
        private bool isEnabled = true;
        public Terrain terrain;
        public int DisableCalls, EnableCalls;
        public SurfaceMapper() => Active.Add(this);
        public bool enabled
        {
            get => isEnabled;
            set
            {
                if (value == isEnabled) return;
                isEnabled = value;
                if (value) { EnableCalls++; Active.Add(this); }
                else { DisableCalls++; Active.Remove(this); }
            }
        }
    }
    public class DiscGroundSolver { }
    public class DiscGroundContact
    {
        public SurfaceMapper mapper;
        public DiscGroundSolver solver;
        public bool onGround;
        public int stepsOffGround;
    }
    public class DiscBehaviour : UnityEngine.Object
    {
        public Terrain terrain;
        public DiscGroundContact groundContact;
        public Transform transform = new();
    }
}
namespace DgmCustomCourses
{
    internal static class CourseScenePatcher { public static bool IsImportedMapActive; }
    internal static class Plugin { public static Logger LogSource = new(); }
    internal class Logger { public void LogInfo(string _) { } }
}
