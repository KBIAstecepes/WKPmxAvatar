namespace WKPmxAvatar
{
    // Profiles identify validated model packs, not arbitrary filenames or peer-provided labels.
    public static class ModelProfiles
    {
        public const string RossiId="pmx:2ddc88f62ea9c801910904aadbb773833c82d0dd0afad6f614c670235de91e46";
        public static string For(string modelId)=>modelId==RossiId?"Rossi":null;
        public static bool SupportsRossiOptions(string modelId)=>For(modelId)=="Rossi";
        public static bool Enabled(string activeModelId,bool savedPreference)=>SupportsRossiOptions(activeModelId)&&savedPreference;
    }
}
