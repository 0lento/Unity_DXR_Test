using System;
using System.IO;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering.HighDefinition
{
    static class EditorDefaultSettings
    {
        static string k_DefaultVolumeAssetPath =
            $@"Packages/com.unity.render-pipelines.high-definition/Editor/RenderPipelineResources/DefaultSettingsVolumeProfile.asset";

        /// <summary>Get the current default VolumeProfile asset. If it is missing, the builtin one is assigned to the current settings.</summary>
        /// <returns>The default VolumeProfile if an HDRenderPipelineAsset is the base SRP asset, null otherwise.</returns>
        internal static VolumeProfile GetOrAssignDefaultVolumeProfile()
        {
            if (!(GraphicsSettings.renderPipelineAsset is HDRenderPipelineAsset hdrpAsset))
                return null;

            if (hdrpAsset.defaultVolumeProfile == null || hdrpAsset.defaultVolumeProfile.Equals(null))
                hdrpAsset.defaultVolumeProfile =
                    AssetDatabase.LoadAssetAtPath<VolumeProfile>(k_DefaultVolumeAssetPath);

            return hdrpAsset.defaultVolumeProfile;
        }
    }
}
