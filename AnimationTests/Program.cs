using Azurite;
using Animation = ScenarioAnimation.ScenarioAnimation;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
var resident = new Il2CppSystem.Collections.Generic.List<Animation> {
    new() { hasCompleted = true }, new() { isCancelled = true } };
Check(AnimationActivity.CountPending(resident) == 0, "resident completed and cancelled animations do not keep a static editor drawing");
Check(!DynamicProducerPolicy.IsDynamicForSurface(true, true, true, false, AnimationActivity.CountPending(resident), 0, 0, false, false, false),
    "completed resident animation list reaches the static production policy");
resident.Add(new());
Check(AnimationActivity.CountPending(resident) == 1, "queued and active animation entries remain protected");
Check(DynamicProducerPolicy.IsDynamicForSurface(true, true, true, false, AnimationActivity.CountPending(resident), 0, 0, false, false, false),
    "one live producer prevents static policy even with completed entries");
resident[2].hasCompleted = true;
Check(AnimationActivity.CountPending(resident) == 0, "completion releases protection without destroying the object");
resident.Add(null!);
Check(AnimationActivity.CountPending(resident) > 0, "unknown animation entry is conservatively protected");
Check(AnimationActivity.CountPending<Animation>(null) == 0, "absent list does not create synthetic animation work");
for (int i = resident.Count; i < 257; i++) resident.Add(new() { hasCompleted = true });
Check(AnimationActivity.CountPending(resident) > 0, "unknown entries in a large list remain protected");
resident.RemoveAt(3);
Check(AnimationActivity.CountPending(resident) == 0, "more than 256 terminal entries can become static");
resident.Add(new());
Check(AnimationActivity.CountPending(resident) == 1, "a live entry at the tail of a large list remains protected");
resident[^1].hasCompleted = true;
for (int i = resident.Count; i < 4097; i++) resident.Add(new() { hasCompleted = true });
Check(AnimationActivity.CountPending(resident) == 1, "lists exceeding 4096 entries remain protected");
Console.WriteLine($"RESULT {passed}/{passed}; production animation observation with host stubs.");

namespace ScenarioAnimation { public class ScenarioAnimation { public bool hasCompleted; public bool isCancelled; } }
namespace Il2CppSystem.Collections.Generic { public class List<T> : System.Collections.Generic.List<T> { } }
