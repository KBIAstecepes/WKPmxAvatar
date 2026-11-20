using BepInEx.Configuration;
namespace WKPmxAvatar {
 // One migration into the WIP opt-in policy; later explicit user choices persist.
 public static class HandAppearanceOptions {
  public const string MenuLabel="Rossi hands (WIP)";
  public static string ToggleLabel(bool enabled)=>MenuLabel+": "+(enabled?"ON":"OFF");
  public static ConfigEntry<bool> Bind(ConfigFile config){
   var hands=config.Bind("Avatar.Rossi","HandsEnabled",false,"WIP first-person hand textures. Off by default; enable manually in Rossi DETAILS. Other model settings and the paw cursor are independent.");
   var migrated=config.Bind("Avatar.Rossi","HandsWipDefaultApplied",false,"Records the one-time WIP default-off migration. Subsequent manual hand-texture choices are preserved.");
   if(!migrated.Value){hands.Value=false;migrated.Value=true;config.Save();}
   return hands;
  }
 }
}
