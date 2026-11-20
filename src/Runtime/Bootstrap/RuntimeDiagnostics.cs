namespace WKPmxAvatar
{
 // Public diagnostics contain fixed text only. IDs, paths and exception details stay in memory.
 // The caller emits each of these finite codes once per plugin lifetime.
 internal static class RuntimeDiagnostics
 {
  internal readonly struct Diagnostic
  {
   internal readonly string Code,Message;
   internal Diagnostic(string code,string message){Code=code;Message=message;}
  }
  internal static Diagnostic Classify(string message)
  {
   if(message==null)return new Diagnostic("R099","Optional avatar operation unavailable; check local resources.");
   if(message.StartsWith("Native remote"))return new Diagnostic("R010","Native motion unavailable; using procedural motion. Check local motion assets.");
   if(message.StartsWith("Assembly grips")||message.StartsWith("Assembly bind"))return new Diagnostic("R011","Assembly grips unavailable; retaining legacy grips. Check the local rig and grip resources.");
   if(message.StartsWith("Accepted disk"))return new Diagnostic("R012","Authored grips unavailable; retaining generic grips.");
   if(message.StartsWith("Animation disabled"))return new Diagnostic("R013","Animation disabled; original model restored.");
   if(message.StartsWith("Avatar selection"))return new Diagnostic("R014","Avatar selection unavailable; check the multiplayer version and local models.");
   if(message.StartsWith("Optional first person"))return new Diagnostic("R015","Optional hand replacement unavailable; original hand sprites retained.");
   if(message.StartsWith("Prompt crouch")||message.StartsWith("Optional pose"))return new Diagnostic("R016","Optional pose synchronization unavailable; check the multiplayer version.");
   if(message.StartsWith("Arm bones"))return new Diagnostic("R017","Arm bones missing; arm IK disabled for this model.");
   return new Diagnostic("R099","Optional avatar operation unavailable; check local resources and model compatibility.");
  }
 }
}
