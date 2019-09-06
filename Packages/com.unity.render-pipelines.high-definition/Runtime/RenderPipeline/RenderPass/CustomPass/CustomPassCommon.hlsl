#ifndef CUSTOM_PASS_COMMON
#define CUSTOM_PASS_COMMON

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassInjectionPoint.cs.hlsl"

float _CustomPassInjectionPoint;

float3 CustomPassSampleCameraColor(float2 uv, float lod)
{
    switch ((int)_CustomPassInjectionPoint)
    {
        case CUSTOMPASSINJECTIONPOINT_BEFORE_RENDERING: return float3(0, 0, 0);
        // there is no color pyramid yet for before transparent so we can't sample with mips.
        // Also, we don't use _RTHandleScaleHistory to sample because the color pyramid bound is the actual camera color buffer which is at the resolution of the camera
        case CUSTOMPASSINJECTIONPOINT_BEFORE_TRANSPARENT: return SAMPLE_TEXTURE2D_X_LOD(_ColorPyramidTexture, s_trilinear_clamp_sampler, uv * _RTHandleScale.xy, 0).rgb;
        default: return SampleCameraColor(uv, lod);
    }
}

struct Attributes
{
    uint vertexID : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vert(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
    return output;
}

#endif // CUSTOM_PASS_COMMON