namespace UnityEngine.Rendering.HighDefinition
{
    [VolumeComponentMenu("Fog/Volumetric Fog")]
    public class VolumetricFog : AtmosphericScattering
    {
        public ColorParameter        albedo                 = new ColorParameter(Color.white);
        public MinFloatParameter     meanFreePath           = new MinFloatParameter(1000000.0f, 1.0f);
        public FloatParameter        baseHeight             = new FloatParameter(0.0f);
        public FloatParameter        maximumHeight          = new FloatParameter(10.0f);
        public ClampedFloatParameter anisotropy             = new ClampedFloatParameter(0.0f, -1.0f, 1.0f);
        public ClampedFloatParameter globalLightProbeDimmer = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
        public BoolParameter         enableDistantFog       = new BoolParameter(false);

        static float ScaleHeightFromLayerDepth(float d)
        {
            // Exp[-d / H] = 0.001
            // -d / H = Log[0.001]
            // H = d / -Log[0.001]
            return d * 0.144765f;
        }

        internal override void PushShaderParameters(HDCamera hdCamera, CommandBuffer cmd)
        {
            PushShaderParametersCommon(hdCamera, cmd, FogType.Volumetric);

            DensityVolumeArtistParameters param = new DensityVolumeArtistParameters(albedo.value, meanFreePath.value, anisotropy.value);

            DensityVolumeEngineData data = param.ConvertToEngineData();

            cmd.SetGlobalVector(HDShaderIDs._HeightFogBaseScattering, data.scattering);
            cmd.SetGlobalFloat(HDShaderIDs._HeightFogBaseExtinction,  data.extinction);

            float crBaseHeight = baseHeight.value;

            if (ShaderConfig.s_CameraRelativeRendering != 0)
            {
                crBaseHeight -= hdCamera.camera.transform.position.y;
            }

            float layerDepth = Mathf.Max(0.01f, maximumHeight.value - baseHeight.value);
            float H          = ScaleHeightFromLayerDepth(layerDepth);

            cmd.SetGlobalVector(HDShaderIDs._HeightFogExponents,  new Vector2(1.0f / H, H));
            cmd.SetGlobalFloat( HDShaderIDs._HeightFogBaseHeight, crBaseHeight);
            cmd.SetGlobalFloat( HDShaderIDs._GlobalFogAnisotropy, anisotropy.value);
            cmd.SetGlobalInt(   HDShaderIDs._EnableDistantFog,    enableDistantFog.value ? 1 : 0);
        }

        public static void PushNeutralShaderParameters(CommandBuffer cmd)
        {
            cmd.SetGlobalVector(HDShaderIDs._HeightFogBaseScattering, Vector3.zero);
            cmd.SetGlobalFloat( HDShaderIDs._HeightFogBaseExtinction, 0.0f);

            cmd.SetGlobalVector(HDShaderIDs._HeightFogExponents,  Vector2.one);
            cmd.SetGlobalFloat( HDShaderIDs._HeightFogBaseHeight, 0.0f);
            cmd.SetGlobalFloat( HDShaderIDs._GlobalFogAnisotropy, 0.0f);
            cmd.SetGlobalInt(   HDShaderIDs._EnableDistantFog,    0);
        }
    }
}
