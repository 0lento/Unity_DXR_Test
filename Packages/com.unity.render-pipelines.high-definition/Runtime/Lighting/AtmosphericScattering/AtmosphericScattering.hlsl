#ifndef UNITY_ATMOSPHERIC_SCATTERING_INCLUDED
#define UNITY_ATMOSPHERIC_SCATTERING_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GeometricTools.hlsl"

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/AtmosphericScattering/AtmosphericScattering.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/VolumetricLighting/VBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSkyCommon.hlsl"

#ifdef DEBUG_DISPLAY
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
#endif

float3 GetFogColor(float3 V, float fragDist)
{
    if (_FogColorMode == FOGCOLORMODE_CONSTANT_COLOR)
    {
        return _FogColor.rgb;
    }
    else if (_FogColorMode == FOGCOLORMODE_SKY_COLOR)
    {
        // Based on Uncharted 4 "Mip Sky Fog" trick: http://advances.realtimerendering.com/other/2016/naughty_dog/NaughtyDog_TechArt_Final.pdf
        float mipLevel = (1.0 - _MipFogMaxMip * saturate((fragDist - _MipFogNear) / (_MipFogFar - _MipFogNear))) * _SkyTextureMipCount;
        // For the atmosphéric scattering, we use the GGX convoluted version of the cubemap. That matches the of the idnex 0
        return SampleSkyTexture(-V, mipLevel, 0).rgb;
    }
    else // Should not be possible.
        return  float3(0.0, 0.0, 0.0);
}

// All units in meters!
// Assumes that there is NO sky occlusion along the ray AT ALL.
// We evaluate atmospheric scattering for the sky and other celestial bodies
// during the sky pass. The opaque atmospheric scattering pass applies atmospheric
// scattering to all other opaque geometry.
void EvaluatePbrAtmosphere(float3 worldSpaceCameraPos, float3 V, float distAlongRay, bool renderSunDisk,
                           out float3 skyColor, out float3 skyOpacity)
{
    skyColor = skyOpacity = 0;

    const float  R = _PlanetaryRadius;
    const float2 n = float2(_AirDensityFalloff, _AerosolDensityFalloff);
    const float2 H = float2(_AirScaleHeight,    _AerosolScaleHeight);

    // TODO: Not sure it's possible to precompute cam rel pos since variables
    // in the two constant buffers may be set at a different frequency?
    const float3 O     = worldSpaceCameraPos * 0.001 - _PlanetCenterPosition; // Convert m to km
    const float  tFrag = distAlongRay * 0.001;                                // Convert m to km

    float3 N; float r; // These params correspond to the entry point
    float  tEntry = IntersectAtmosphere(O, V, N, r).x;
    float  tExit  = IntersectAtmosphere(O, V, N, r).y;

    float NdotV  = dot(N, V);
    float cosChi = -NdotV;
    float cosHor = ComputeCosineOfHorizonAngle(r);

    bool rayIntersectsAtmosphere = (tEntry >= 0);
    bool lookAboveHorizon        = (cosChi >= cosHor);

    // If it's outside the atmosphere, we only need one texture look-up.
    bool rayEndsInsideAtmosphere = tFrag * (1 + 2 * FLT_EPS) < tExit;

    // Our precomputed tables only contain information above ground.
    if (!lookAboveHorizon) // See the ground?
    {
        float tGround = tEntry + IntersectSphere(R, cosChi, r).x;

        // Being on or below ground still counts as outside.
        rayEndsInsideAtmosphere = tFrag * (1 + 2 * FLT_EPS) < min(tGround, tExit);
    }

    if (rayIntersectsAtmosphere)
    {
        float2 Z = R * n;
        float r0 = r, cosChi0 = cosChi;

        float r1, cosChi1;
        float3 N1;

        if (tFrag < tExit)
        {
            float3 P1 = O + tFrag * -V;

            r1      = length(P1);
            N1      = P1 * rcp(r1);
            cosChi1 = dot(P1, -V) * rcp(r1);

            // Potential swap.
            cosChi0 = (cosChi1 >= 0) ? cosChi0 : -cosChi0;
        }

        float2 ch0, ch1 = 0;

        {
            float2 z0 = r0 * n;

            ch0.x = RescaledChapmanFunction(z0.x, Z.x, cosChi0);
            ch0.y = RescaledChapmanFunction(z0.y, Z.y, cosChi0);
        }

        if (tFrag < tExit)
        {
            float2 z1 = r1 * n;

            ch1.x = ChapmanUpperApprox(z1.x, abs(cosChi1)) * exp(Z.x - z1.x);
            ch1.y = ChapmanUpperApprox(z1.y, abs(cosChi1)) * exp(Z.y - z1.y);
        }

        // We may have swapped X and Y.
        float2 ch = abs(ch0 - ch1);

        float3 optDepth = ch.x * H.x * _AirSeaLevelExtinction
                        + ch.y * H.y * _AerosolSeaLevelExtinction;

        skyOpacity = 1 - TransmittanceFromOpticalDepth(optDepth); // from 'tEntry' to 'tFrag'


        for (uint i = 0; i < _DirectionalLightCount; i++)
        {
            DirectionalLightData light = _DirectionalLightDatas[i];

            // Use scalar or integer cores (more efficient).
            bool interactsWithSky = asint(light.distanceFromCamera) >= 0;

            if (!interactsWithSky) continue;

            float3 L = -light.forward.xyz;

            // The sun disk hack causes some issues when applied to nearby geometry, so don't do that.
            if (renderSunDisk && asint(light.angularDiameter) != 0 && light.distanceFromCamera <= tFrag)
            {
                float c = dot(L, -V);

                if (-0.99999 < c && c < 0.99999)
                {
                    float alpha = 0.5 * light.angularDiameter;
                    float beta  = acos(c);
                    float gamma = min(alpha, beta);

                    // Make sure that if (beta = Pi), no rotation is performed.
                    gamma *= (PI - beta) * rcp(PI - gamma);

                    // Perform a shortest arc rotation.
                    float3   A = normalize(cross(L, -V));
                    float3x3 R = RotationFromAxisAngle(A, sin(gamma), cos(gamma));

                    // Rotate the light direction.
                    L = mul(R, L);
                }
            }

            // TODO: solve in spherical coords?
            float height = r - R;
            float  NdotL = dot(N, L);
            float3 projL = L - N * NdotL;
            float3 projV = V - N * NdotV;
            float  phiL  = acos(clamp(dot(projL, projV) * rsqrt(max(dot(projL, projL) * dot(projV, projV), FLT_EPS)), -1, 1));

            TexCoord4D tc = ConvertPositionAndOrientationToTexCoords(height, NdotV, NdotL, phiL);

            float3 radiance = 0; // from 'tEntry' to 'tExit'

            // Single scattering does not contain the phase function.
            float LdotV = dot(L, V);

            // Air.
            radiance += lerp(SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture,     s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                             SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture,     s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                             tc.a) * AirPhase(LdotV);

            // Aerosols.
            // TODO: since aerosols are in a separate texture,
            // they could use a different max height value for improved precision.
            radiance += lerp(SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                             SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                             tc.a) * AerosolPhase(LdotV);

            // MS.
            radiance += lerp(SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture,      s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                             SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture,      s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                             tc.a);

            if (rayEndsInsideAtmosphere)
            {
                float3 radiance1 = 0; // from 'tFrag' to 'tExit'

                // TODO: solve in spherical coords?
                float height1 = r1 - R;
                float  NdotV1 = -cosChi1;
                float  NdotL1 = dot(N1, L);
                float3 projL1 = L - N1 * NdotL1;
                float3 projV1 = V - N1 * NdotV1;
                float  phiL1  = acos(clamp(dot(projL1, projV1) * rsqrt(max(dot(projL1, projL1) * dot(projV1, projV1), FLT_EPS)), -1, 1));

                tc = ConvertPositionAndOrientationToTexCoords(height1, NdotV1, NdotL1, phiL1);

                // Single scattering does not contain the phase function.

                // Air.
                radiance1 += lerp(SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture,     s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                                  SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture,     s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                                  tc.a) * AirPhase(LdotV);

                // Aerosols.
                // TODO: since aerosols are in a separate texture,
                // they could use a different max height value for improved precision.
                radiance1 += lerp(SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                                  SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                                  tc.a) * AerosolPhase(LdotV);

                // MS.
                radiance1 += lerp(SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture,      s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                                  SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture,      s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                                  tc.a);

                // L(tEntry, tFrag) = L(tEntry, tExit) - T(tEntry, tFrag) * L(tFrag, tExit)
                radiance = max(0, radiance - (1 - skyOpacity) * radiance1);
            }

            radiance *= light.color.rgb; // Globally scale the intensity

            skyColor += radiance;
        }

    }
}

float3 GetViewForwardDir1(float4x4 viewMatrix)
{
    return -viewMatrix[2].xyz;
}

// The color is premultiplied by the opacity.
void EvaluateAtmosphericScattering(PositionInputs posInput, float3 V, out float3 color, out float3 opacity)
{
    color = opacity = 0;

#ifdef DEBUG_DISPLAY
    // Don't sample atmospheric scattering when lighting debug more are enabled so fog is not visible
    if (_DebugShadowMapMode == SHADOWMAPDEBUGMODE_SINGLE_SHADOW || _DebugLightingMode == DEBUGLIGHTINGMODE_DIFFUSE_LIGHTING || _DebugLightingMode == DEBUGLIGHTINGMODE_SPECULAR_LIGHTING || _DebugLightingMode == DEBUGLIGHTINGMODE_LUX_METER)
        return;
#endif

    // TODO: do not recompute this, but rather pass it directly.
    // Note1: remember the hacked value of 'posInput.positionWS'.
    // Note2: we do not adjust it anymore to account for the distance to the planet. This can lead to wrong results (since the planet does not write depth).
    float fogFragDist = distance(posInput.positionWS, GetCurrentViewPosition());

    float3 skyColor = 0, skyOpacity = 0;

    bool hasPbrSkyAtmosphere = (_AtmosphericScatteringType & 128) == 128;

    // We apply atmospheric scattering to all celestial bodies during the sky pass.
    // Unfortunately, they don't write depth.
    if (hasPbrSkyAtmosphere && (posInput.deviceDepth != UNITY_RAW_FAR_CLIP_VALUE))
    {
        // Convert it to distance along the ray. Doesn't work with tilt shift, etc.
        float tFrag = posInput.linearDepth * rcp(dot(-V, GetViewForwardDir1(UNITY_MATRIX_V)));

        EvaluatePbrAtmosphere(_WorldSpaceCameraPos, V, tFrag, false, skyColor, skyOpacity);

        skyColor *= _IntensityMultiplier * GetCurrentExposureMultiplier();
    }

    float3 fogColor = 0, fogOpacity = 0;

    int fogType = _AtmosphericScatteringType & 127;

    switch (fogType)
    {
        case FOGTYPE_LINEAR:
        {
            fogColor   = GetFogColor(V, fogFragDist);
            fogOpacity = _FogDensity * saturate((fogFragDist - _LinearFogStart) * _LinearFogOneOverRange) * saturate((_LinearFogHeightEnd - GetAbsolutePositionWS(posInput.positionWS).y) * _LinearFogHeightOneOverRange);
            fogColor  *= fogOpacity;
            break;
        }
        case FOGTYPE_EXPONENTIAL:
        {
            float fogHeight = max(0.0, GetAbsolutePositionWS(posInput.positionWS).y - _ExpFogBaseHeight);
            fogColor   = GetFogColor(V, fogFragDist);
            fogOpacity = _FogDensity * TransmittanceHomogeneousMedium(_ExpFogHeightAttenuation, fogHeight) * (1.0f - TransmittanceHomogeneousMedium(1.0f / _ExpFogDistance, fogFragDist));
            fogColor  *= fogOpacity;
            break;
        }
        case FOGTYPE_VOLUMETRIC:
        {
            float4 value = SampleVBuffer(TEXTURE3D_ARGS(_VBufferLighting, s_linear_clamp_sampler),
                                         posInput.positionNDC,
                                         fogFragDist,
                                         _VBufferResolution,
                                         _VBufferUvScaleAndLimit.xy,
                                         _VBufferUvScaleAndLimit.zw,
                                         _VBufferDistanceEncodingParams,
                                         _VBufferDistanceDecodingParams,
                                         true, false);

            // TODO: add some slowly animated noise (dither?) to the reconstructed value.
            // TODO: re-enable tone mapping after implementing pre-exposure.
            float4 volFog = DelinearizeRGBA(float4(/*FastTonemapInvert*/(value.rgb), value.a));

            // TODO: if 'posInput.linearDepth' is computed using 'posInput.positionWS',
            // and the latter resides on the far plane, the computation will be numerically unstable.
            float distDelta = fogFragDist - _VBufferLastSliceDist;

            if ((_EnableDistantFog != 0) && (distDelta > 0))
            {
                // Apply the distant (fallback) fog.
                float3 positionWS  = GetCurrentViewPosition() - V * _VBufferLastSliceDist;
                float  startHeight = positionWS.y;
                float  cosZenith   = -V.y;

                // For both homogeneous and exponential media,
                // Integrate[Transmittance[x] * Scattering[x], {x, 0, t}] = Albedo * Opacity[t].
                // Note that pulling the incoming radiance (which is affected by the fog) out of the
                // integral is wrong, as it means that shadow rays are not volumetrically shadowed.
                // This will result in fog looking overly bright.

                float3 volAlbedo  = _HeightFogBaseScattering / _HeightFogBaseExtinction;
                float  odFallback = OpticalDepthHeightFog(_HeightFogBaseExtinction, _HeightFogBaseHeight,
                                                          _HeightFogExponents, cosZenith, startHeight, distDelta);
                float  trFallback = TransmittanceFromOpticalDepth(odFallback);
                float  trCamera   = 1 - volFog.a;

                volFog.rgb += trCamera * GetFogColor(V, fogFragDist) * volAlbedo * (1 - trFallback);
                volFog.a    = 1 - (trCamera * trFallback);
            }

            fogColor   = volFog.rgb; // Pre-multiplied by design
            fogOpacity = volFog.a;
            break;
        }
    }

    fogColor *= GetCurrentExposureMultiplier();

    // Rendering of fog and atmospheric scattering cannot really be decoupled.
#if 0
    // The best workaround is to deep composite them.
    float3 fogOD = OpticalDepthFromOpacity(fogOpacity);

    float3 fogRatio;
           fogRatio.r = (fogOpacity.r >= FLT_EPS) ? (fogOD.r * rcp(fogOpacity.r)) : 1;
           fogRatio.g = (fogOpacity.g >= FLT_EPS) ? (fogOD.g * rcp(fogOpacity.g)) : 1;
           fogRatio.b = (fogOpacity.b >= FLT_EPS) ? (fogOD.b * rcp(fogOpacity.b)) : 1;
    float3 skyRatio;
           skyRatio.r = (skyOpacity.r >= FLT_EPS) ? (skyOD.r * rcp(skyOpacity.r)) : 1;
           skyRatio.g = (skyOpacity.g >= FLT_EPS) ? (skyOD.g * rcp(skyOpacity.g)) : 1;
           skyRatio.b = (skyOpacity.b >= FLT_EPS) ? (skyOD.b * rcp(skyOpacity.b)) : 1;

    float3 logFogColor = fogRatio * fogColor;
    float3 logSkyColor = skyRatio * skyColor;

    float3 logCompositeColor = logFogColor + logSkyColor;
    float3 compositeOD       = fogOD + skyOD;

    opacity = OpacityFromOpticalDepth(compositeOD);

    float3 rcpCompositeRatio;
           rcpCompositeRatio.r = (opacity.r >= FLT_EPS) ? (opacity.r * rcp(compositeOD.r)) : 1;
           rcpCompositeRatio.g = (opacity.g >= FLT_EPS) ? (opacity.g * rcp(compositeOD.g)) : 1;
           rcpCompositeRatio.b = (opacity.b >= FLT_EPS) ? (opacity.b * rcp(compositeOD.b)) : 1;

    color = rcpCompositeRatio * logCompositeColor;
#else
    // Deep compositing assumes that the fog spans the same range as the atmosphere.
    // Our fog is short range, so deep compositing gives surprising results.
    // Using the "shallow" over operator is more appropriate in our context.
    // We could do something more clever with deep compositing, but this would
    // probably be a waste in terms of perf.
    CompositeOver(fogColor, fogOpacity, skyColor, skyOpacity,
                  color, opacity);
#endif
}

#endif
