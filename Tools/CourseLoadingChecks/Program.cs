using DgmCustomCourses;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}

var gate = new CourseSelectionLoadGate();
gate.Begin(100, 10f);
Check(!gate.ReadyToPrepare(101), "waits for the loading screen to render before preparation");
Check(gate.ReadyToPrepare(102), "allows preparation after two frames");
Check(!gate.ObserveLayout(1200f, 800f), "growing layout does not release the screen");
Check(!gate.ObserveLayout(1200f, 1200f), "first stable layout pass waits");
Check(!gate.ObserveLayout(1200f, 1200f), "second stable layout pass waits");
Check(gate.ObserveLayout(1200f, 1200f), "third stable layout pass releases the screen");
gate.ResetLayout();
Check(!gate.ObserveLayout(float.NaN, 1200f), "invalid layout height cannot settle");
Check(!gate.ObserveLayout(30100f, 1200f), "unreasonable layout height cannot settle");
Check(!gate.TimedOut(17.9f), "selector does not time out early");
Check(gate.TimedOut(18f), "selector timeout releases an unavailable layout");
gate.End();
Check(!gate.TimedOut(100f) && !gate.ReadyToPrepare(1000), "ended loading remains inactive");

var defaultHole = System.Text.Json.JsonSerializer.Deserialize<HoleDefinition>("{}");
Check(!defaultHole.TeeEnabled && !defaultHole.CardEnabled && defaultHole.BasketEnabled, "tee and card default off while the basket remains visible");
var teeOnly = System.Text.Json.JsonSerializer.Deserialize<HoleDefinition>("{\"teeEnabled\":true,\"cardEnabled\":false}");
var cardOnly = System.Text.Json.JsonSerializer.Deserialize<HoleDefinition>("{\"teeEnabled\":false,\"cardEnabled\":true}");
Check(teeOnly.TeeEnabled && !teeOnly.CardEnabled, "tee can be enabled independently");
Check(!cardOnly.TeeEnabled && cardOnly.CardEnabled, "card can be enabled independently");
