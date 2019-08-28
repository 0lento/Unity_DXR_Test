using UnityEditor.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering.HighDefinition
{
    [CanEditMultipleObjects]
    [VolumeComponentEditor(typeof(AmbientOcclusion))]
    class AmbientOcclusionEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Intensity;
        SerializedDataParameter m_StepCount;
        SerializedDataParameter m_Radius;
        SerializedDataParameter m_FullResolution;
        SerializedDataParameter m_MaximumRadiusInPixels;
        SerializedDataParameter m_DirectLightingStrength;

        // Ray Tracing parameters
        SerializedDataParameter m_RayTracing;
        SerializedDataParameter m_RayLength;
        SerializedDataParameter m_SampleCount;
        SerializedDataParameter m_Denoise;
        SerializedDataParameter m_DenoiserRadius;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<AmbientOcclusion>(serializedObject);

            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_StepCount = Unpack(o.Find(x => x.stepCount));
            m_Radius = Unpack(o.Find(x => x.radius));
            m_FullResolution = Unpack(o.Find(x => x.fullResolution));
            m_MaximumRadiusInPixels = Unpack(o.Find(x => x.maximumRadiusInPixels));

            m_DirectLightingStrength = Unpack(o.Find(x => x.directLightingStrength));

            m_RayTracing = Unpack(o.Find(x => x.rayTracing));
            m_RayLength = Unpack(o.Find(x => x.rayLength));

            m_Denoise = Unpack(o.Find(x => x.denoise));
            m_SampleCount = Unpack(o.Find(x => x.sampleCount));
            m_DenoiserRadius = Unpack(o.Find(x => x.denoiserRadius));
        }

        public override void OnInspectorGUI()
        {
            if (!(GraphicsSettings.renderPipelineAsset as HDRenderPipelineAsset)
                ?.currentPlatformRenderPipelineSettings.supportSSAO ?? false)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("The current HDRP Asset does not support Ambient Occlusion.", MessageType.Error, wide: true);
                return;
            }

#if ENABLE_RAYTRACING
            PropertyField(m_RayTracing, EditorGUIUtility.TrTextContent("Ray Tracing", "Enable ray traced ambient occlusion."));
#endif

            // Shared attributes
            PropertyField(m_Intensity, EditorGUIUtility.TrTextContent("Intensity", "Controls the strength of the ambient occlusion effect. Increase this value to produce darker areas."));
            PropertyField(m_DirectLightingStrength, EditorGUIUtility.TrTextContent("Direct Lighting Strength", "Controls how much the ambient light affects occlusion."));

#if ENABLE_RAYTRACING
            if (m_RayTracing.overrideState.boolValue && m_RayTracing.value.boolValue)
            {
                PropertyField(m_RayLength, EditorGUIUtility.TrTextContent("Ray Length", "Controls the length of ambient occlusion rays."));
                PropertyField(m_SampleCount, EditorGUIUtility.TrTextContent("Sample Count", "Number of samples for ray traced ambient occlusion."));
                PropertyField(m_Denoise, EditorGUIUtility.TrTextContent("Denoise", "Enable denoising on the ray traced ambient occlusion."));
                {
                    EditorGUI.indentLevel++;
                    PropertyField(m_DenoiserRadius, EditorGUIUtility.TrTextContent("Denoiser Radius", "Radius parameter for the denoising."));
                    EditorGUI.indentLevel--;
                }
            }
            else
#endif
            {
                PropertyField(m_Radius, EditorGUIUtility.TrTextContent("Radius", "Sampling radius. Bigger the radius, wider AO will be achieved, risking to lose fine details and increasing cost of the effect due to increasing cache misses."));
                PropertyField(m_MaximumRadiusInPixels, EditorGUIUtility.TrTextContent("Maximum Radius In Pixels", "This poses a maximum radius in pixels that we consider. It is very important to keep this as tight as possible to preserve good performance. Note that this is the value used for 1080p when *not* running the effect at full resolution, it will be scaled accordingly for other resolutions."));
                PropertyField(m_FullResolution, EditorGUIUtility.TrTextContent("Full Resolution", "The effect runs at full resolution. This increases quality, but also decreases performance significantly."));
                PropertyField(m_StepCount, EditorGUIUtility.TrTextContent("Step Count", "Number of steps to take along one signed direction during horizon search (this is the number of steps in positive and negative direction)."));
            }
        }
    }
}
