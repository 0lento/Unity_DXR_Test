using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.Serialization;

namespace UnityEngine.Rendering.HighDefinition
{
    // This enum extent the original LightType enum with new light type from HD
    public enum LightTypeExtent
    {
        Punctual, // Fallback on LightShape type
        Rectangle,
        Tube,
        // Sphere,
        // Disc,
    };

    public enum SpotLightShape { Cone, Pyramid, Box };

    public enum LightUnit
    {
        Lumen,      // lm = total power/flux emitted by the light
        Candela,    // lm/sr = flux per steradian
        Lux,        // lm/m² = flux per unit area
        Luminance,  // lm/m²/sr = flux per unit area and per steradian
        Ev100,      // ISO 100 Exposure Value (https://en.wikipedia.org/wiki/Exposure_value)
    }

    internal enum DirectionalLightUnit
    {
        Lux = LightUnit.Lux,
    }

    internal enum AreaLightUnit
    {
        Lumen = LightUnit.Lumen,
        Luminance = LightUnit.Luminance,
        Ev100 = LightUnit.Ev100,
    }

    internal enum PunctualLightUnit
    {
        Lumen = LightUnit.Lumen,
        Candela = LightUnit.Candela,
        Lux = LightUnit.Lux,
        Ev100 = LightUnit.Ev100
    }

    /// <summary>
    /// Shadow Update mode
    /// </summary>
    public enum ShadowUpdateMode
    {
        EveryFrame = 0,
        OnEnable,
        OnDemand
    }

    /// <summary>
    /// Shadow Resolution Tier
    /// </summary>
    public enum ShadowResolutionTier
    {
        Low = 0,
        Medium,
        High,
        VeryHigh
    }


    // Light layering
    public enum LightLayerEnum
    {
        Nothing = 0,   // Custom name for "Nothing" option
        LightLayerDefault = 1 << 0,
        LightLayer1 = 1 << 1,
        LightLayer2 = 1 << 2,
        LightLayer3 = 1 << 3,
        LightLayer4 = 1 << 4,
        LightLayer5 = 1 << 5,
        LightLayer6 = 1 << 6,
        LightLayer7 = 1 << 7,
        Everything = 0xFF, // Custom name for "Everything" option
    }

    // Note: do not use internally, this enum only exists for the user API to set the light type
    /// <summary>
    /// Type of an HDRP Light
    /// </summary>
    public enum HDLightType
    {
        Point,
        BoxSpot,
        PyramidSpot,
        ConeSpot,
        Directional,
        Rectangle,
        Tube,
    }

    public static class HDLightTypeExtension
    {
        /// <summary>
        /// Returns true if the hd light type is a spot light
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static bool IsSpot(this HDLightType type) => type == HDLightType.BoxSpot || type == HDLightType.PyramidSpot || type == HDLightType.ConeSpot;

        /// <summary>
        /// Returns true if the hd light type is an area light
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static bool IsArea(this HDLightType type) => type == HDLightType.Tube || type == HDLightType.Rectangle;
    }

    // This structure contains all the old values for every recordable fields from the HD light editor
    // so we can force timeline to record changes on other fields from the LateUpdate function (editor only)
    struct TimelineWorkaround
    {
        public float oldSpotAngle;
        public Color oldLightColor;
        public Vector3 oldLossyScale;
        public bool oldDisplayAreaLightEmissiveMesh;
        public float oldLightColorTemperature;
        public float oldIntensity;
    }

    //@TODO: We should continuously move these values
    // into the engine when we can see them being generally useful
    [HelpURL(Documentation.baseURL + Documentation.version + Documentation.subURL + "Light-Component" + Documentation.endURL)]
    [RequireComponent(typeof(Light))]
    [ExecuteAlways]
    public partial class HDAdditionalLightData : MonoBehaviour
    {
        /// <summary>
        /// The default intensity value for directional lights in Lux
        /// </summary>
        public const float k_DefaultDirectionalLightIntensity = Mathf.PI; // In lux
        /// <summary>
        /// The default intensity value for punctual lights in Lumen
        /// </summary>
        public const float k_DefaultPunctualLightIntensity = 600.0f;      // Light default to 600 lumen, i.e ~48 candela
        /// <summary>
        /// The default intensity value for area lights in Lumen
        /// </summary>
        public const float k_DefaultAreaLightIntensity = 200.0f;          // Light default to 200 lumen to better match point light

        /// <summary>
        /// Minimum value for the spot light angle
        /// </summary>
        public const float k_MinSpotAngle = 1.0f;
        /// <summary>
        /// Maximum value for the spot light angle
        /// </summary>
        public const float k_MaxSpotAngle = 179.0f;

        /// <summary>
        /// Minimum aspect ratio for pyramid spot lights
        /// </summary>
        public const float k_MinAspectRatio = 0.05f;
        /// <summary>
        /// Maximum aspect ratio for pyramid spot lights
        /// </summary>
        public const float k_MaxAspectRatio = 20.0f;

        /// <summary>
        /// Minimum shadow map view bias scale
        /// </summary>
        public const float k_MinViewBiasScale = 0.0f;
        /// <summary>
        /// Maximum shadow map view bias scale
        /// </summary>
        public const float k_MaxViewBiasScale = 15.0f;

        /// <summary>
        /// Minimum area light size
        /// </summary>
        public const float k_MinAreaWidth = 0.01f; // Provide a small size of 1cm for line light

        /// <summary>
        /// Default shadow resolution
        /// </summary>
        public const int k_DefaultShadowResolution = 512;

        // EVSM limits
        internal const float k_MinEvsmExponent = 5.0f;
        internal const float k_MaxEvsmExponent = 42.0f;
        internal const float k_MinEvsmLightLeakBias = 0.0f;
        internal const float k_MaxEvsmLightLeakBias = 1.0f;
        internal const float k_MinEvsmVarianceBias = 0.0f;
        internal const float k_MaxEvsmVarianceBias = 0.001f;
        internal const int k_MinEvsmBlurPasses = 0;
        internal const int k_MaxEvsmBlurPasses = 8;

        internal const float k_MinSpotInnerPercent = 0.0f;
        internal const float k_MaxSpotInnerPercent = 100.0f;

        internal const float k_MinAreaLightShadowCone = 10.0f;
        internal const float k_MaxAreaLightShadowCone = 179.0f;

#region HDLight Properties API

        [SerializeField, FormerlySerializedAs("displayLightIntensity")]
        float m_Intensity;
        /// <summary>
        /// Get/Set the intensity of the light using the current light unit.
        /// </summary>
        public float intensity
        {
            get => m_Intensity;
            set
            {
                if (m_Intensity == value)
                    return;

                m_Intensity = Mathf.Clamp(value, 0, float.MaxValue);
                UpdateLightIntensity();
            }
        }

        // Only for Spotlight, should be hide for other light
        [SerializeField, FormerlySerializedAs("enableSpotReflector")]
        bool m_EnableSpotReflector = false;
        /// <summary>
        /// Get/Set the Spot Reflection option on spot lights.
        /// </summary>
        public bool enableSpotReflector
        {
            get => m_EnableSpotReflector;
            set
            {
                if (m_EnableSpotReflector == value)
                    return;

                m_EnableSpotReflector = value;
                UpdateLightIntensity();
            }
        }

        // Lux unity for all light except directional require a distance
        [SerializeField, FormerlySerializedAs("luxAtDistance")]
        float m_LuxAtDistance = 1.0f;
        /// <summary>
        /// Set/Get the distance for spot lights where the emission intensity is matches the value set in the intensity property.
        /// </summary>
        public float luxAtDistance
        {
            get => m_LuxAtDistance;
            set
            {
                if (m_LuxAtDistance == value)
                    return;

                m_LuxAtDistance = Mathf.Clamp(value, 0, float.MaxValue);
                UpdateLightIntensity();
            }
        }

        [Range(k_MinSpotInnerPercent, k_MaxSpotInnerPercent)]
        [SerializeField]
        float m_InnerSpotPercent; // To display this field in the UI this need to be public
        /// <summary>
        /// Get/Set the inner spot radius in percent.
        /// </summary>
        public float innerSpotPercent
        {
            get => m_InnerSpotPercent;
            set
            {
                if (m_InnerSpotPercent == value)
                    return;

                m_InnerSpotPercent = Mathf.Clamp(value, k_MinSpotInnerPercent, k_MaxSpotInnerPercent);
                UpdateLightIntensity();
            }
        }

        /// <summary>
        /// Get the inner spot radius between 0 and 1.
        /// </summary>
        public float innerSpotPercent01 => innerSpotPercent / 100f;

        [Range(0.0f, 1.0f)]
        [SerializeField, FormerlySerializedAs("lightDimmer")]
        float m_LightDimmer = 1.0f;
        /// <summary>
        /// Get/Set the light dimmer.
        /// </summary>
        public float lightDimmer
        {
            get => m_LightDimmer;
            set
            {
                if (m_LightDimmer == value)
                    return;

                m_LightDimmer = Mathf.Clamp01(value);
            }
        }

        [Range(0.0f, 1.0f), SerializeField, FormerlySerializedAs("volumetricDimmer")]
        float m_VolumetricDimmer = 1.0f;
        /// <summary>
        /// Get/Set the light dimmer on volumetric effects, between 0 and 1.
        /// </summary>
        public float volumetricDimmer
        {
            get => m_VolumetricDimmer;
            set
            {
                if (m_VolumetricDimmer == value)
                    return;

                m_VolumetricDimmer = Mathf.Clamp01(value);
            }
        }

        // Used internally to convert any light unit input into light intensity
        [SerializeField, FormerlySerializedAs("lightUnit")]
        LightUnit m_LightUnit = LightUnit.Lumen;
        /// <summary>
        /// Get/Set the light unit. When changing the light unit, the intensity will be converted to match the previous intensity in the new unit.
        /// </summary>
        public LightUnit lightUnit
        {
            get => m_LightUnit;
            set
            {
                if (m_LightUnit == value)
                    return;

                if (!IsValidLightUnitForType(legacyLight.type, m_LightTypeExtent, value))
                {
                    var supportedTypes = String.Join(", ", GetSupportedLightUnits(legacyLight.type, m_LightTypeExtent));
                    Debug.LogError($"Set Light Unit '{value}' to a {GetLightTypeName()} is not allowed, only {supportedTypes} are supported.");
                    return;
                }

                LightUtils.ConvertLightIntensity(m_LightUnit, value, this, legacyLight);

                m_LightUnit = value;
                UpdateLightIntensity();
            }
        }

        // Not used for directional lights.
        [SerializeField, FormerlySerializedAs("fadeDistance")]
        float m_FadeDistance = 10000.0f;
        /// <summary>
        /// Get/Set the light fade distance.
        /// </summary>
        public float fadeDistance
        {
            get => m_FadeDistance;
            set
            {
                if (m_FadeDistance == value)
                    return;

                m_FadeDistance = Mathf.Clamp(value, 0, float.MaxValue);
            }
        }

        [SerializeField, FormerlySerializedAs("affectDiffuse")]
        bool m_AffectDiffuse = true;
        /// <summary>
        /// Controls whether the light affects the diffuse or not
        /// </summary>
        public bool affectDiffuse
        {
            get => m_AffectDiffuse;
            set
            {
                if (m_AffectDiffuse == value)
                    return;

                m_AffectDiffuse = value;
            }
        }

        [SerializeField, FormerlySerializedAs("affectSpecular")]
        bool m_AffectSpecular = true;
        /// <summary>
        /// Controls whether the light affects the specular or not
        /// </summary>
        public bool affectSpecular
        {
            get => m_AffectSpecular;
            set
            {
                if (m_AffectSpecular == value)
                    return;

                m_AffectSpecular = value;
            }
        }

        // This property work only with shadow mask and allow to say we don't render any lightMapped object in the shadow map
        [SerializeField, FormerlySerializedAs("nonLightmappedOnly")]
        bool m_NonLightmappedOnly = false;
        /// <summary>
        /// Only used when the shadow masks are enabled, control if the we use ShadowMask or DistanceShadowmask for this light.
        /// </summary>
        public bool nonLightmappedOnly
        {
            get => m_NonLightmappedOnly;
            set
            {
                if (m_NonLightmappedOnly == value)
                    return;

                m_NonLightmappedOnly = value;
                legacyLight.lightShadowCasterMode = value ? LightShadowCasterMode.NonLightmappedOnly : LightShadowCasterMode.Everything;
            }
        }

        [SerializeField, FormerlySerializedAs("lightTypeExtent")]
        LightTypeExtent m_LightTypeExtent = LightTypeExtent.Punctual;
        /// <summary>
        /// Control the area light type. When set to an area Light type, also set the Light.type to Point.
        /// </summary>
        public LightTypeExtent lightTypeExtent
        {
            get => m_LightTypeExtent;
            set
            {
                // Here we don't have self assignation protection because the light type is stored in two
                // fields, this one and the legacyLight.type. So if the legacy light type changes we can't
                // know if it's useless or not to run the code below.

                if (IsAreaLight(value))
                {
                    legacyLight.type = LightType.Point;
                }

                var supportedUnits = GetSupportedLightUnits(legacyLight.type, value);
                // If the current light unit is not supported by the new light type, we change it
                if (!supportedUnits.Any(u => u == lightUnit))
                    lightUnit = supportedUnits.First();

                m_LightTypeExtent = value;
                UpdateAllLightValues();
            }
        }

        // Only for Spotlight, should be hide for other light
        [SerializeField, FormerlySerializedAs("spotLightShape")]
        SpotLightShape m_SpotLightShape = SpotLightShape.Cone;
        /// <summary>
        /// Control the shape of the spot light.
        /// </summary>
        public SpotLightShape spotLightShape
        {
            get => m_SpotLightShape;
            set
            {
                if (m_SpotLightShape == value)
                    return;

                m_SpotLightShape = value;
                UpdateAllLightValues();
            }
        }

        // Only for Rectangle/Line/box projector lights.
        [SerializeField, FormerlySerializedAs("shapeWidth")]
        float m_ShapeWidth = 0.5f;
        /// <summary>
        /// Control the width of an area, a box spot light or a directional light cookie.
        /// </summary>
        public float shapeWidth
        {
            get => m_ShapeWidth;
            set
            {
                if (m_ShapeWidth == value)
                    return;

                if (IsAreaLight(m_LightTypeExtent))
                    m_ShapeWidth = Mathf.Clamp(value, k_MinAreaWidth, float.MaxValue);
                else
                    m_ShapeWidth = Mathf.Clamp(value, 0, float.MaxValue);
                UpdateAllLightValues();
            }
        }

        // Only for Rectangle/box projector and rectangle area lights
        [SerializeField, FormerlySerializedAs("shapeHeight")]
        float m_ShapeHeight = 0.5f;
        /// <summary>
        /// Control the height of an area, a box spot light or a directional light cookie.
        /// </summary>
        public float shapeHeight
        {
            get => m_ShapeHeight;
            set
            {
                if (m_ShapeHeight == value)
                    return;

                if (IsAreaLight(m_LightTypeExtent))
                    m_ShapeHeight = Mathf.Clamp(value, k_MinAreaWidth, float.MaxValue);
                else
                    m_ShapeHeight = Mathf.Clamp(value, 0, float.MaxValue);
                UpdateAllLightValues();
            }
        }

        // Only for pyramid projector
        [SerializeField, FormerlySerializedAs("aspectRatio")]
        float m_AspectRatio = 1.0f;
        /// <summary>
        /// Get/Set the aspect ratio of a pyramid light
        /// </summary>
        public float aspectRatio
        {
            get => m_AspectRatio;
            set
            {
                if (m_AspectRatio == value)
                    return;

                m_AspectRatio = Mathf.Clamp(value, k_MinAspectRatio, k_MaxAspectRatio);
                UpdateAllLightValues();
            }
        }

        // Only for Punctual/Sphere/Disc
        [SerializeField, FormerlySerializedAs("shapeRadius")]
        float m_ShapeRadius = 0.0f;
        /// <summary>
        /// Get/Set the radius of a light
        /// </summary>
        public float shapeRadius
        {
            get => m_ShapeRadius;
            set
            {
                if (m_ShapeRadius == value)
                    return;

                m_ShapeRadius = Mathf.Clamp(value, 0, float.MaxValue);
                UpdateAllLightValues();
            }
        }

        [SerializeField, FormerlySerializedAs("useCustomSpotLightShadowCone")]
        bool m_UseCustomSpotLightShadowCone = false;
        // Custom spot angle for spotlight shadows
        /// <summary>
        /// Toggle the custom spot light shadow cone.
        /// </summary>
        public bool useCustomSpotLightShadowCone
        {
            get => m_UseCustomSpotLightShadowCone;
            set
            {
                if (m_UseCustomSpotLightShadowCone == value)
                    return;

                m_UseCustomSpotLightShadowCone = value;
            }
        }

        [SerializeField, FormerlySerializedAs("customSpotLightShadowCone")]
        float m_CustomSpotLightShadowCone = 30.0f;
        /// <summary>
        /// Get/Set the custom spot shadow cone value.
        /// </summary>
        /// <value></value>
        public float customSpotLightShadowCone
        {
            get => m_CustomSpotLightShadowCone;
            set
            {
                if (m_CustomSpotLightShadowCone == value)
                    return;

                m_CustomSpotLightShadowCone = value;
            }
        }

        // Only for Spot/Point/Directional - use to cheaply fake specular spherical area light
        // It is not 1 to make sure the highlight does not disappear.
        [Range(0.0f, 1.0f)]
        [SerializeField, FormerlySerializedAs("maxSmoothness")]
        float m_MaxSmoothness = 0.99f;
        /// <summary>
        /// Get/Set the maximum smoothness of a punctual or directional light.
        /// </summary>
        public float maxSmoothness
        {
            get => m_MaxSmoothness;
            set
            {
                if (m_MaxSmoothness == value)
                    return;

                m_MaxSmoothness = Mathf.Clamp01(value);
            }
        }

        // If true, we apply the smooth attenuation factor on the range attenuation to get 0 value, else the attenuation is just inverse square and never reach 0
        [SerializeField, FormerlySerializedAs("applyRangeAttenuation")]
        bool m_ApplyRangeAttenuation = true;
        /// <summary>
        /// If enabled, apply a smooth attenuation factor so at the end of the range, the attenuation is 0.
        /// Otherwise the inverse-square attenuation is used and the value never reaches 0.
        /// </summary>
        public bool applyRangeAttenuation
        {
            get => m_ApplyRangeAttenuation;
            set
            {
                if (m_ApplyRangeAttenuation == value)
                    return;

                m_ApplyRangeAttenuation = value;
                UpdateAllLightValues();
            }
        }

        // When true, a mesh will be display to represent the area light (Can only be change in editor, component is added in Editor)
        [SerializeField, FormerlySerializedAs("displayAreaLightEmissiveMesh")]
        bool m_DisplayAreaLightEmissiveMesh = false;
        /// <summary>
        /// If enabled, display an emissive mesh rect synchronized with the intensity and color of the light.
        /// </summary>
        internal bool displayAreaLightEmissiveMesh
        {
            get => m_DisplayAreaLightEmissiveMesh;
            set
            {
                if (m_DisplayAreaLightEmissiveMesh == value)
                    return;

                m_DisplayAreaLightEmissiveMesh = value;

                UpdateAllLightValues();
            }
        }

        // Optional cookie for rectangular area lights
        [SerializeField, FormerlySerializedAs("areaLightCookie")]
        Texture m_AreaLightCookie = null;
        /// <summary>
        /// Get/Set cookie texture for area lights.
        /// </summary>
        public Texture areaLightCookie
        {
            get => m_AreaLightCookie;
            set
            {
                if (m_AreaLightCookie == value)
                    return;

                m_AreaLightCookie = value;
                UpdateAllLightValues();
            }
        }

        [Range(k_MinAreaLightShadowCone, k_MaxAreaLightShadowCone)]
        [SerializeField, FormerlySerializedAs("areaLightShadowCone")]
        float m_AreaLightShadowCone = 120.0f;
        /// <summary>
        /// Get/Set area light shadow cone value.
        /// </summary>
        public float areaLightShadowCone
        {
            get => m_AreaLightShadowCone;
            set
            {
                if (m_AreaLightShadowCone == value)
                    return;

                m_AreaLightShadowCone = Mathf.Clamp(value, k_MinAreaLightShadowCone, k_MaxAreaLightShadowCone);
                UpdateAllLightValues();
            }
        }

        // Flag that tells us if the shadow should be screen space
        [SerializeField, FormerlySerializedAs("useScreenSpaceShadows")]
        bool m_UseScreenSpaceShadows = false;
        /// <summary>
        /// Controls if we resolve the directional light shadows in screen space (ray tracing only).
        /// </summary>
        public bool useScreenSpaceShadows
        {
            get => m_UseScreenSpaceShadows;
            set
            {
                if (m_UseScreenSpaceShadows == value)
                    return;

                m_UseScreenSpaceShadows = value;
            }
        }

        // Directional lights only.
        [SerializeField, FormerlySerializedAs("interactsWithSky")]
        bool m_InteractsWithSky = true;
        /// <summary>
        /// Controls if the directional light affect the Physically Based sky.
        /// This have no effect on other skies.
        /// </summary>
        public bool interactsWithSky
        {
            get => m_InteractsWithSky;
            set
            {
                if (m_InteractsWithSky == value)
                    return;

                m_InteractsWithSky = value;
            }
        }

        [SerializeField, FormerlySerializedAs("angularDiameter")]
        float m_AngularDiameter = 0;
        /// <summary>
        /// Angular diameter of the emissive celestial body represented by the light as seen from the camera (in degrees).
        /// Used to render the sun/moon disk.
        /// </summary>
        public float angularDiameter
        {
            get => m_AngularDiameter;
            set
            {
                if (m_AngularDiameter == value)
                    return;

                m_AngularDiameter = value;
            }
        }

        [SerializeField, FormerlySerializedAs("distance")]
        float m_Distance = 150000000.0f; // Sun to Earth
        /// <summary>
        /// Distance from the camera to the emissive celestial body represented by the light.
        /// </summary>
        public float distance
        {
            get => m_Distance;
            set
            {
                if (m_Distance == value)
                    return;

                m_Distance = value;
            }
        }

#if ENABLE_RAYTRACING
        [SerializeField, FormerlySerializedAs("useRayTracedShadows")]
        bool m_UseRayTracedShadows = false;
        /// <summary>
        /// Controls if we use ray traced shadows.
        /// </summary>
        public bool useRayTracedShadows
        {
            get => m_UseRayTracedShadows;
            set
            {
                if (m_UseRayTracedShadows == value)
                    return;

                m_UseRayTracedShadows = value;
            }
        }

        [Range(1, 32)]
        [SerializeField, FormerlySerializedAs("numRayTracingSamples")]
        int m_NumRayTracingSamples = 4;
        /// <summary>
        /// Controls the number of sample used for the ray traced shadows.
        /// </summary>
        public int numRayTracingSamples
        {
            get => m_NumRayTracingSamples;
            set
            {
                if (m_NumRayTracingSamples == value)
                    return;

                m_NumRayTracingSamples = Mathf.Clamp(value, 1, 32);
            }
        }

        [SerializeField, FormerlySerializedAs("filterTracedShadow")]
        bool m_FilterTracedShadow = true;
        /// <summary>
        /// Toggle the filtering of ray traced shadows.
        /// </summary>
        public bool filterTracedShadow
        {
            get => m_FilterTracedShadow;
            set
            {
                if (m_FilterTracedShadow == value)
                    return;

                m_FilterTracedShadow = value;
            }
        }

        [Range(1, 32)]
        [SerializeField, FormerlySerializedAs("filterSizeTraced")]
        int m_FilterSizeTraced = 16;
        /// <summary>
        /// Control the size of the filter used for ray traced shadows
        /// </summary>
        public int filterSizeTraced
        {
            get => m_FilterSizeTraced;
            set
            {
                if (m_FilterSizeTraced == value)
                    return;

                m_FilterSizeTraced = Mathf.Clamp(value, 1, 32);
            }
        }

        [Range(0.0f, 2.0f)]
        [SerializeField, FormerlySerializedAs("sunLightConeAngle")]
        float m_SunLightConeAngle = 0.5f;
        /// <summary>
        /// Angular size of the sun in degree.
        /// </summary>
        public float sunLightConeAngle
        {
            get => m_SunLightConeAngle;
            set
            {
                if (m_SunLightConeAngle == value)
                    return;

                m_SunLightConeAngle = Mathf.Clamp(value, 0.0f, 2.0f);
            }
        }
#endif

        [Range(k_MinEvsmExponent, k_MaxEvsmExponent)]
        [SerializeField, FormerlySerializedAs("evsmExponent")]
        float m_EvsmExponent = 15.0f;
        /// <summary>
        /// Controls the exponent used for EVSM shadows.
        /// </summary>
        public float evsmExponent
        {
            get => m_EvsmExponent;
            set
            {
                if (m_EvsmExponent == value)
                    return;

                m_EvsmExponent = Mathf.Clamp(value, k_MinEvsmExponent, k_MaxEvsmExponent);
            }
        }

        [Range(k_MinEvsmLightLeakBias, k_MaxEvsmLightLeakBias)]
        [SerializeField, FormerlySerializedAs("evsmLightLeakBias")]
        float m_EvsmLightLeakBias = 0.0f;
        /// <summary>
        /// Controls the light leak bias value for EVSM shadows.
        /// </summary>
        public float evsmLightLeakBias
        {
            get => m_EvsmLightLeakBias;
            set
            {
                if (m_EvsmLightLeakBias == value)
                    return;

                m_EvsmLightLeakBias = Mathf.Clamp(value, k_MinEvsmLightLeakBias, k_MaxEvsmLightLeakBias);
            }
        }

        [Range(k_MinEvsmVarianceBias, k_MaxEvsmVarianceBias)]
        [SerializeField, FormerlySerializedAs("evsmVarianceBias")]
        float m_EvsmVarianceBias = 1e-5f;
        /// <summary>
        /// Controls the variance bias used for EVSM shadows.
        /// </summary>
        public float evsmVarianceBias
        {
            get => m_EvsmVarianceBias;
            set
            {
                if (m_EvsmVarianceBias == value)
                    return;

                m_EvsmVarianceBias = Mathf.Clamp(value, k_MinEvsmVarianceBias, k_MaxEvsmVarianceBias);
            }
        }

        [Range(k_MinEvsmBlurPasses, k_MaxEvsmBlurPasses)]
        [SerializeField, FormerlySerializedAs("evsmBlurPasses")]
        int m_EvsmBlurPasses = 0;
        /// <summary>
        /// Controls the number of blur passes used for EVSM shadows.
        /// </summary>
        public int evsmBlurPasses
        {
            get => m_EvsmBlurPasses;
            set
            {
                if (m_EvsmBlurPasses == value)
                    return;

                m_EvsmBlurPasses = Mathf.Clamp(value, k_MinEvsmBlurPasses, k_MaxEvsmBlurPasses);
            }
        }

        // Now the renderingLayerMask is used for shadow layers and not light layers
        [SerializeField, FormerlySerializedAs("lightlayersMask")]
        LightLayerEnum m_LightlayersMask = LightLayerEnum.LightLayerDefault;
        /// <summary>
        /// Light Layers used for shadows only, for default Light Layers use Light.renderingLayerMask
        /// </summary>
        /// <value></value>
        public LightLayerEnum lightlayersMask
        {
            get => linkShadowLayers ? (LightLayerEnum)RenderingLayerMaskToLightLayer(legacyLight.renderingLayerMask) : m_LightlayersMask;
            set
            {
                if (m_LightlayersMask == value)
                    return;

                m_LightlayersMask = value;
            }
        }

        [SerializeField, FormerlySerializedAs("linkShadowLayers")]
        bool m_LinkShadowLayers = true;
        /// <summary>
        /// Controls if we want to synchronize shadow map light layers and light layers or not.
        /// </summary>
        public bool linkShadowLayers
        {
            get => m_LinkShadowLayers;
            set
            {
                if (m_LinkShadowLayers == value)
                    return;

                m_LinkShadowLayers = value;
            }
        }

        /// <summary>
        /// Returns a mask of light layers as uint and handle the case of Everything as being 0xFF and not -1
        /// </summary>
        /// <returns></returns>
        public uint GetLightLayers()
        {
            int value = (int)lightlayersMask;
            return value < 0 ? (uint)LightLayerEnum.Everything : (uint)value;
        }

        // Shadow Settings
        [SerializeField, FormerlySerializedAs("shadowNearPlane")]
        float    m_ShadowNearPlane = 0.1f;
        /// <summary>
        /// Controls the near plane distance of the shadows.
        /// </summary>
        public float shadowNearPlane
        {
            get => m_ShadowNearPlane;
            set
            {
                if (m_ShadowNearPlane == value)
                    return;

                m_ShadowNearPlane = Mathf.Clamp(value, HDShadowUtils.k_MinShadowNearPlane, HDShadowUtils.k_MaxShadowNearPlane);
            }
        }

        // PCSS settings
        [Range(0, 1.0f)]
        [SerializeField, FormerlySerializedAs("shadowSoftness")]
        float    m_ShadowSoftness = .5f;
        /// <summary>
        /// Controls how much softness you want for PCSS shadows.
        /// </summary>
        public float shadowSoftness
        {
            get => m_ShadowSoftness;
            set
            {
                if (m_ShadowSoftness == value)
                    return;

                m_ShadowSoftness = Mathf.Clamp01(value);
            }
        }

        [Range(1, 64)]
        [SerializeField, FormerlySerializedAs("blockerSampleCount")]
        int      m_BlockerSampleCount = 24;
        /// <summary>
        /// Controls the number of samples used to detect blockers for PCSS shadows.
        /// </summary>
        public int blockerSampleCount
        {
            get => m_BlockerSampleCount;
            set
            {
                if (m_BlockerSampleCount == value)
                    return;

                m_BlockerSampleCount = Mathf.Clamp(value, 1, 64);
            }
        }

        [Range(1, 64)]
        [SerializeField, FormerlySerializedAs("filterSampleCount")]
        int      m_FilterSampleCount = 16;
        /// <summary>
        /// Controls the number of samples used to filter for PCSS shadows.
        /// </summary>
        public int filterSampleCount
        {
            get => m_FilterSampleCount;
            set
            {
                if (m_FilterSampleCount == value)
                    return;

                m_FilterSampleCount = Mathf.Clamp(value, 1, 64);
            }
        }

        [Range(0, 0.001f)]
        [SerializeField, FormerlySerializedAs("minFilterSize")]
        float m_MinFilterSize = 0.00001f;
        /// <summary>
        /// Controls the minimum filter size of PCSS shadows.
        /// </summary>
        public float minFilterSize
        {
            get => m_MinFilterSize;
            set
            {
                if (m_MinFilterSize == value)
                    return;

                m_MinFilterSize = Mathf.Clamp(value, 0.0f, 0.001f);
            }
        }

        // Improved Moment Shadows settings
        [Range(1, 32)]
        [SerializeField, FormerlySerializedAs("kernelSize")]
        int m_KernelSize = 5;
        /// <summary>
        /// Controls the kernel size for IMSM shadows.
        /// </summary>
        public int kernelSize
        {
            get => m_KernelSize;
            set
            {
                if (m_KernelSize == value)
                    return;

                m_KernelSize = Mathf.Clamp(value, 1, 32);
            }
        }

        [Range(0.0f, 9.0f)]
        [SerializeField, FormerlySerializedAs("lightAngle")]
        float m_LightAngle = 1.0f;
        /// <summary>
        /// Controls the light angle for IMSM shadows.
        /// </summary>
        public float lightAngle
        {
            get => m_LightAngle;
            set
            {
                if (m_LightAngle == value)
                    return;

                m_LightAngle = Mathf.Clamp(value, 0.0f, 9.0f);
            }
        }

        [Range(0.0001f, 0.01f)]
        [SerializeField, FormerlySerializedAs("maxDepthBias")]
        float m_MaxDepthBias = 0.001f;
        /// <summary>
        /// Controls the max depth bias for IMSM shadows.
        /// </summary>
        public float maxDepthBias
        {
            get => m_MaxDepthBias;
            set
            {
                if (m_MaxDepthBias == value)
                    return;

                m_MaxDepthBias = Mathf.Clamp(value, 0.0001f, 0.01f);
            }
        }

        /// <summary>
        /// The range of the light.
        /// </summary>
        /// <value></value>
        public float range
        {
            get => legacyLight.range;
            set => legacyLight.range = value;
        }

        /// <summary>
        /// Color of the light.
        /// </summary>
        public Color color
        {
            get => legacyLight.color;
            set
            {
                legacyLight.color = value;

                // Update Area Light Emissive mesh color
                UpdateAreaLightEmissiveMesh();
            }
        }

        #endregion

        #region HDShadow Properties API (from AdditionalShadowData)

        [SerializeField]
        ShadowResolutionTier m_ShadowResolutionTier = ShadowResolutionTier.Medium;
        /// <summary>
        /// Get/Set the quality level for shadow map resoluton.
        /// </summary>
        public ShadowResolutionTier shadowResolutionTier
        {
            get => m_ShadowResolutionTier;
            set
            {
                if (m_ShadowResolutionTier == value)
                    return;

                m_ShadowResolutionTier = value;
            }
        }

        [SerializeField]
        bool m_UseShadowQualitySettings = false;
        /// <summary>
        /// Toggle the usage of quality settings to determine shadow resolution.
        /// </summary>
        public bool useShadowQualitySettings
        {
            get => m_UseShadowQualitySettings;
            set
            {
                if (m_UseShadowQualitySettings == value)
                    return;

                m_UseShadowQualitySettings = value;
            }
        }


        [SerializeField]
        int m_CustomShadowResolution = k_DefaultShadowResolution;
        /// <summary>
        /// Get/Set the resolution of shadow maps in case quality settings are not used.
        /// </summary>
        /// <value></value>
        public int customResolution
        {
            get => m_CustomShadowResolution;
            set
            {
                if (m_CustomShadowResolution == value)
                    return;

                m_CustomShadowResolution = Mathf.Clamp(value, HDShadowManager.k_MinShadowMapResolution, HDShadowManager.k_MaxShadowMapResolution);
            }
        }

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float m_ShadowDimmer = 1.0f;
        /// <summary>
        /// Get/Set the shadow dimmer.
        /// </summary>
        public float shadowDimmer
        {
            get => m_ShadowDimmer;
            set
            {
                if (m_ShadowDimmer == value)
                    return;

                m_ShadowDimmer = Mathf.Clamp01(value);
            }
        }

        [Range(0.0f, 1.0f)]
        [SerializeField]
        float m_VolumetricShadowDimmer = 1.0f;
        /// <summary>
        /// Get/Set the volumetric shadow dimmer value, between 0 and 1.
        /// </summary>
        public float volumetricShadowDimmer
        {
            get => m_VolumetricShadowDimmer;
            set
            {
                if (m_VolumetricShadowDimmer == value)
                    return;

                m_VolumetricShadowDimmer = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        float m_ShadowFadeDistance = 10000.0f;
        /// <summary>
        /// Get/Set the shadow fade distance.
        /// </summary>
        public float shadowFadeDistance
        {
            get => m_ShadowFadeDistance;
            set
            {
                if (m_ShadowFadeDistance == value)
                    return;

                m_ShadowFadeDistance = Mathf.Clamp(value, 0, float.MaxValue);
            }
        }

        [SerializeField]
        bool m_ContactShadows = false;
        /// <summary>
        /// Toggle the contact shadows.
        /// </summary>
        public bool contactShadows
        {
            get => m_ContactShadows;
            set
            {
                if (m_ContactShadows == value)
                    return;

                m_ContactShadows = value;
            }
        }

        [SerializeField]
        Color m_ShadowTint = Color.black;
        /// <summary>
        /// Controls the tint of the shadows.
        /// </summary>
        /// <value></value>
        public Color shadowTint
        {
            get => m_ShadowTint;
            set
            {
                if (m_ShadowTint == value)
                    return;

                m_ShadowTint = value;
            }
        }

        [SerializeField]
        float m_NormalBias = 0.75f;
        /// <summary>
        /// Get/Set the normal bias of the shadow maps.
        /// </summary>
        /// <value></value>
        public float normalBias
        {
            get => m_NormalBias;
            set
            {
                if (m_NormalBias == value)
                    return;

                m_NormalBias = value;
            }
        }

        [SerializeField]
        float m_ConstantBias = 0.15f;
        /// <summary>
        /// Get/Set the constant bias of the shadow maps.
        /// </summary>
        /// <value></value>
        public float constantBias
        {
            get => m_ConstantBias;
            set
            {
                if (m_ConstantBias == value)
                    return;

                m_ConstantBias = value;
            }
        }

        [SerializeField]
        ShadowUpdateMode m_ShadowUpdateMode = ShadowUpdateMode.EveryFrame;
        /// <summary>
        /// Get/Set the shadow update mode.
        /// </summary>
        /// <value></value>
        public ShadowUpdateMode shadowUpdateMode
        {
            get => m_ShadowUpdateMode;
            set
            {
                if (m_ShadowUpdateMode == value)
                    return;

                m_ShadowUpdateMode = value;
            }
        }

#endregion

#region Internal API for moving shadow datas from AdditionalShadowData to HDAdditionalLightData

        [SerializeField]
        float[] m_ShadowCascadeRatios = new float[3] { 0.05f, 0.2f, 0.3f };
        internal float[] shadowCascadeRatios
        {
            get => m_ShadowCascadeRatios;
            set => m_ShadowCascadeRatios = value;
        }

        [SerializeField]
        float[] m_ShadowCascadeBorders = new float[4] { 0.2f, 0.2f, 0.2f, 0.2f };
        internal float[] shadowCascadeBorders
        {
            get => m_ShadowCascadeBorders;
            set => m_ShadowCascadeBorders = value;
        }

        [SerializeField]
        int m_ShadowAlgorithm = 0;
        internal int shadowAlgorithm
        {
            get => m_ShadowAlgorithm;
            set => m_ShadowAlgorithm = value;
        }

        [SerializeField]
        int m_ShadowVariant = 0;
        internal int shadowVariant
        {
            get => m_ShadowVariant;
            set => m_ShadowVariant = value;
        }

        [SerializeField]
        int m_ShadowPrecision = 0;
        internal int shadowPrecision
        {
            get => m_ShadowPrecision;
            set => m_ShadowPrecision = value;
        }

#endregion

#pragma warning disable 0414 // The field '...' is assigned but its value is never used, these fields are used by the inspector
        // This is specific for the LightEditor GUI and not use at runtime
        [SerializeField, FormerlySerializedAs("useOldInspector")]
        bool useOldInspector = false;
        [SerializeField, FormerlySerializedAs("useVolumetric")]
        bool useVolumetric = true;
        [SerializeField, FormerlySerializedAs("featuresFoldout")]
        bool featuresFoldout = true;
        [SerializeField, FormerlySerializedAs("showAdditionalSettings")]
        byte showAdditionalSettings = 0;
#pragma warning restore 0414

        HDShadowRequest[]   shadowRequests;
        bool                m_WillRenderShadowMap;
        bool                m_WillRenderScreenSpaceShadow;
#if ENABLE_RAYTRACING
        bool                m_WillRenderRayTracedShadow;
#endif
        int[]               m_ShadowRequestIndices;
        bool                m_ShadowMapRenderedSinceLastRequest = false;

        // Data for cached shadow maps.
        Vector2             m_CachedShadowResolution = new Vector2(0,0);
        Vector3             m_CachedViewPos = new Vector3(0, 0, 0);

        int[]               m_CachedResolutionRequestIndices = new int[6];
        bool                m_CachedDataIsValid = true;
        // This is useful to detect whether the atlas has been repacked since the light was last seen
        int                 m_AtlasShapeID = 0;

        [System.NonSerialized]
        Plane[]             m_ShadowFrustumPlanes = new Plane[6];

        #if ENABLE_RAYTRACING
        // Temporary index that stores the current shadow index for the light
        [System.NonSerialized] internal int shadowIndex;
        #endif

        [System.NonSerialized] HDShadowSettings    _ShadowSettings = null;
        HDShadowSettings    m_ShadowSettings
        {
            get
            {
                if (_ShadowSettings == null)
                    _ShadowSettings = VolumeManager.instance.stack.GetComponent<HDShadowSettings>();
                return _ShadowSettings;
            }
        }

        // Runtime datas used to compute light intensity
        Light m_Light;
        internal Light legacyLight
        {
            get
            {
                if (m_Light == null)
                    m_Light = GetComponent<Light>();
                return m_Light;
            }
        }

        private void DisableCachedShadowSlot()
        {
            ShadowMapType shadowMapType = (lightTypeExtent == LightTypeExtent.Rectangle) ? ShadowMapType.AreaLightAtlas :
                  (legacyLight.type != LightType.Directional) ? ShadowMapType.PunctualAtlas : ShadowMapType.CascadedDirectional;

            if (WillRenderShadowMap() && !ShadowIsUpdatedEveryFrame())
            {
                HDShadowManager.instance.MarkCachedShadowSlotsAsEmpty(shadowMapType, GetInstanceID());
            }
        }

        void OnDestroy()
        {
            DisableCachedShadowSlot();
        }

        void OnDisable()
        {
            DisableCachedShadowSlot();
        }

        int GetShadowRequestCount()
        {
            return (legacyLight.type == LightType.Point && lightTypeExtent == LightTypeExtent.Punctual) ? 6 : (legacyLight.type == LightType.Directional) ? m_ShadowSettings.cascadeShadowSplitCount.value : 1;
        }

        internal void RequestShadowMapRendering()
        {
            if(shadowUpdateMode == ShadowUpdateMode.OnDemand)
                m_ShadowMapRenderedSinceLastRequest = false;
        }
        internal bool ShouldRenderShadows()
        {
            switch (shadowUpdateMode)
            {
                case ShadowUpdateMode.EveryFrame:
                    return true;
                case ShadowUpdateMode.OnDemand:
                    return !m_ShadowMapRenderedSinceLastRequest;
                case ShadowUpdateMode.OnEnable:
                    return !m_ShadowMapRenderedSinceLastRequest;
            }
            return true;
        }

        internal bool ShadowIsUpdatedEveryFrame()
        {
            return shadowUpdateMode == ShadowUpdateMode.EveryFrame;
        }

        internal void EvaluateShadowState(HDCamera hdCamera, CullingResults cullResults, FrameSettings frameSettings, int lightIndex)
        {
            Bounds bounds;
            float cameraDistance = Vector3.Distance(hdCamera.camera.transform.position, transform.position);

            m_WillRenderShadowMap = legacyLight.shadows != LightShadows.None && frameSettings.IsEnabled(FrameSettingsField.ShadowMaps);

            m_WillRenderShadowMap &= cullResults.GetShadowCasterBounds(lightIndex, out bounds);
            // When creating a new light, at the first frame, there is no AdditionalShadowData so we can't really render shadows
            m_WillRenderShadowMap &= shadowDimmer > 0;
            // If the shadow is too far away, we don't render it
            m_WillRenderShadowMap &= legacyLight.type == LightType.Directional || cameraDistance < shadowFadeDistance;

            // First we reset the ray tracing and screen space shadow data
            m_WillRenderScreenSpaceShadow = false;
#if ENABLE_RAYTRACING
            m_WillRenderRayTracedShadow = false;
#endif

            // If this camera does not allow screen space shadows we are done, set the target parameters to false and leave the function
            if (!hdCamera.frameSettings.IsEnabled(FrameSettingsField.ScreenSpaceShadows) || !m_WillRenderShadowMap)
                return;

#if ENABLE_RAYTRACING
            // We render screen space shadows if we are a ray traced rectangle area light or a screen space directional light shadow
            if ((m_UseRayTracedShadows && lightTypeExtent == LightTypeExtent.Rectangle)
                || (useScreenSpaceShadows && legacyLight.type == LightType.Directional))
            {
                m_WillRenderScreenSpaceShadow = true;
            }

            // We will evaluate a ray traced shadow if we a ray traced area shadow
            if ((m_UseRayTracedShadows && lightTypeExtent == LightTypeExtent.Rectangle)
                || (m_UseRayTracedShadows && legacyLight.type == LightType.Directional))
            {
                m_WillRenderRayTracedShadow = true;
            }
#endif
        }

        private int GetResolutionFromSettings(ShadowMapType shadowMapType, HDShadowInitParameters initParameters)
        {
            bool customRes = !useShadowQualitySettings;
            switch (shadowMapType)
            {
                case ShadowMapType.CascadedDirectional:
                    return customRes ? Math.Min(customResolution, initParameters.maxDirectionalShadowMapResolution) : initParameters.directionalLightsResolutionTiers.GetResolution(shadowResolutionTier);
                case ShadowMapType.PunctualAtlas:
                    return customRes ? Math.Min(customResolution, initParameters.maxPunctualShadowMapResolution) : initParameters.punctualLightsResolutionTiers.GetResolution(shadowResolutionTier);
                case ShadowMapType.AreaLightAtlas:
                    return customRes ? Math.Min(customResolution, initParameters.maxAreaShadowMapResolution) : initParameters.areaLightsResolutionTiers.GetResolution(shadowResolutionTier);
            }

            return 0;
        }

        internal void ReserveShadowMap(Camera camera, HDShadowManager shadowManager, HDShadowInitParameters initParameters, Rect screenRect)
        {
            if (!m_WillRenderShadowMap)
                return;

            // Create shadow requests array using the light type
            if (shadowRequests == null || m_ShadowRequestIndices == null)
            {
                const int maxLightShadowRequestsCount = 6;
                shadowRequests = new HDShadowRequest[maxLightShadowRequestsCount];
                m_ShadowRequestIndices = new int[maxLightShadowRequestsCount];

                for (int i = 0; i < maxLightShadowRequestsCount; i++)
                {
                    shadowRequests[i] = new HDShadowRequest();
                }
            }

            // Reserver wanted resolution in the shadow atlas
            ShadowMapType shadowMapType = (lightTypeExtent == LightTypeExtent.Rectangle) ? ShadowMapType.AreaLightAtlas :
                                          (legacyLight.type != LightType.Directional) ? ShadowMapType.PunctualAtlas : ShadowMapType.CascadedDirectional;

            int resolution = GetResolutionFromSettings(shadowMapType, initParameters);
            Vector2 viewportSize = new Vector2(resolution, resolution);

            bool viewPortRescaling = false;

            // Compute dynamic shadow resolution
            viewPortRescaling |= (shadowMapType == ShadowMapType.PunctualAtlas && initParameters.punctualLightShadowAtlas.useDynamicViewportRescale);
            viewPortRescaling |= (shadowMapType == ShadowMapType.AreaLightAtlas && initParameters.areaLightShadowAtlas.useDynamicViewportRescale);

            bool shadowsAreCached = !ShouldRenderShadows();
            if (shadowsAreCached)
            {
                viewportSize = m_CachedShadowResolution;
            }
            else
            {
                m_CachedShadowResolution = viewportSize;
            }

            if (viewPortRescaling && !shadowsAreCached)
            {
                // resize viewport size by the normalized size of the light on screen
                float screenArea = screenRect.width * screenRect.height;
                viewportSize *= Mathf.Lerp(64f / viewportSize.x, 1f, screenArea);
                viewportSize = Vector2.Max(new Vector2(64f, 64f) / viewportSize, viewportSize);

                // Prevent flickering caused by the floating size of the viewport
                viewportSize.x = Mathf.Round(viewportSize.x);
                viewportSize.y = Mathf.Round(viewportSize.y);
            }

            viewportSize = Vector2.Max(viewportSize, new Vector2(HDShadowManager.k_MinShadowMapResolution, HDShadowManager.k_MinShadowMapResolution));

            // Update the directional shadow atlas size
            if (legacyLight.type == LightType.Directional)
                shadowManager.UpdateDirectionalShadowResolution((int)viewportSize.x, m_ShadowSettings.cascadeShadowSplitCount.value);

            int count = GetShadowRequestCount();
            bool needsCachedSlotsInAtlas = !(ShadowIsUpdatedEveryFrame() || legacyLight.type == LightType.Directional);

            for (int index = 0; index < count; index++)
            {
                m_ShadowRequestIndices[index] = shadowManager.ReserveShadowResolutions(needsCachedSlotsInAtlas ? new Vector2(resolution, resolution) : viewportSize, shadowMapType, GetInstanceID(), index, needsCachedSlotsInAtlas, out m_CachedResolutionRequestIndices[index]);
            }
        }

        internal bool WillRenderShadowMap()
        {
            return m_WillRenderShadowMap;
        }

        internal bool WillRenderScreenSpaceShadow()
        {
            return m_WillRenderScreenSpaceShadow;
        }

#if ENABLE_RAYTRACING
        internal bool WillRenderRayTracedShadow()
        {
            return m_WillRenderRayTracedShadow;
        }
#endif

        // This offset shift the position of the spotlight used to approximate the area light shadows. The offset is the minimum such that the full
        // area light shape is included in the cone spanned by the spot light.
        internal static float GetAreaLightOffsetForShadows(Vector2 shapeSize, float coneAngle)
        {
            float rectangleDiagonal = shapeSize.magnitude;
            float halfAngle = coneAngle * 0.5f;
            float cotanHalfAngle = 1.0f / Mathf.Tan(halfAngle * Mathf.Deg2Rad);
            float offset = rectangleDiagonal * cotanHalfAngle;

            return -offset;
        }

        private float GetDirectionalConstantBias(int index, float sphereRadius, float resolution)
        {
            // TODO: Heuristics here can possibly be improved with more data points.

            const float baseBias = 2.0f;
            float range = 0.0f;
            if (index == 0)
            {
                range = m_ShadowSettings.cascadeShadowSplits[0];
            }
            else if (index == 3)
            {
                range = 1 - m_ShadowSettings.cascadeShadowSplits[2];
            }
            else
            {
                range = m_ShadowSettings.cascadeShadowSplits[index] - m_ShadowSettings.cascadeShadowSplits[index - 1];
            }

            range *= m_ShadowSettings.maxShadowDistance.value;
            float texelScale = (sphereRadius * sphereRadius) / resolution;

            return Math.Min(0.02f, constantBias * texelScale * baseBias / range);
        }

        private void UpdateDirectionalShadowRequest(HDShadowManager manager, VisibleLight visibleLight, CullingResults cullResults, Vector2 viewportSize, int requestIndex, int lightIndex, Vector3 cameraPos, HDShadowRequest shadowRequest, out Matrix4x4 invViewProjection)
        {
            Vector4 cullingSphere;
            float nearPlaneOffset = QualitySettings.shadowNearPlaneOffset;

            HDShadowUtils.ExtractDirectionalLightData(
                visibleLight, viewportSize, (uint)requestIndex, m_ShadowSettings.cascadeShadowSplitCount.value,
                m_ShadowSettings.cascadeShadowSplits, nearPlaneOffset, cullResults, lightIndex,
                out shadowRequest.view, out invViewProjection, out shadowRequest.deviceProjectionYFlip,
                out shadowRequest.deviceProjection, out shadowRequest.splitData
            );

            cullingSphere = shadowRequest.splitData.cullingSphere;

            // Camera relative for directional light culling sphere
            if (ShaderConfig.s_CameraRelativeRendering != 0)
            {
                cullingSphere.x -= cameraPos.x;
                cullingSphere.y -= cameraPos.y;
                cullingSphere.z -= cameraPos.z;
            }

			shadowRequest.constantBias = GetDirectionalConstantBias(requestIndex, cullingSphere.w, viewportSize.x);
            manager.UpdateCascade(requestIndex, cullingSphere, m_ShadowSettings.cascadeShadowBorders[requestIndex]);
        }

        // Must return the first executed shadow request
        internal int UpdateShadowRequest(HDCamera hdCamera, HDShadowManager manager, VisibleLight visibleLight, CullingResults cullResults, int lightIndex, LightingDebugSettings lightingDebugSettings, out int shadowRequestCount)
        {
            int                 firstShadowRequestIndex = -1;
            Vector3             cameraPos = hdCamera.mainViewConstants.worldSpaceCameraPos;
            shadowRequestCount = 0;

            int count = GetShadowRequestCount();
            bool shadowIsCached = !ShouldRenderShadows() && !lightingDebugSettings.clearShadowAtlas;
            bool isUpdatedEveryFrame = ShadowIsUpdatedEveryFrame();
            for (int index = 0; index < count; index++)
            {
                var         shadowRequest = shadowRequests[index];

                Matrix4x4   invViewProjection = Matrix4x4.identity;
                int         shadowRequestIndex = m_ShadowRequestIndices[index];

                ShadowMapType shadowMapType = (lightTypeExtent == LightTypeExtent.Rectangle) ? ShadowMapType.AreaLightAtlas :
                              (legacyLight.type != LightType.Directional) ? ShadowMapType.PunctualAtlas : ShadowMapType.CascadedDirectional;

                bool hasCachedSlotInAtlas = !(ShadowIsUpdatedEveryFrame() || legacyLight.type == LightType.Directional);

                bool shouldUseRequestFromCachedList = hasCachedSlotInAtlas && !manager.AtlasHasResized(shadowMapType);
                bool cachedDataIsValid =  m_CachedDataIsValid && (manager.GetAtlasShapeID(shadowMapType) == m_AtlasShapeID) && manager.CachedDataIsValid(shadowMapType);
                HDShadowResolutionRequest resolutionRequest = manager.GetResolutionRequest(shadowMapType, shouldUseRequestFromCachedList, shouldUseRequestFromCachedList ? m_CachedResolutionRequestIndices[index] : shadowRequestIndex);

                if (resolutionRequest == null)
                    continue;

                Vector2 viewportSize = resolutionRequest.resolution;

                cachedDataIsValid = cachedDataIsValid || (legacyLight.type == LightType.Directional);
                shadowIsCached = shadowIsCached && (hasCachedSlotInAtlas && cachedDataIsValid || legacyLight.type == LightType.Directional);

                if (shadowRequestIndex == -1)
                    continue;

                if (shadowIsCached)
                {
                    shadowRequest.cachedShadowData.cacheTranslationDelta = cameraPos - m_CachedViewPos;
                    shadowRequest.shouldUseCachedShadow = true;

                    // If directional we still need to calculate the split data.
                    if (legacyLight.type == LightType.Directional)
                        UpdateDirectionalShadowRequest(manager, visibleLight, cullResults, viewportSize, index, lightIndex, cameraPos, shadowRequest, out invViewProjection);

                }
                else
                {
                    m_CachedViewPos = cameraPos;
                    shadowRequest.shouldUseCachedShadow = false;
                    m_ShadowMapRenderedSinceLastRequest = true;

                    if (lightTypeExtent == LightTypeExtent.Rectangle)
                    {
                        Vector2 shapeSize = new Vector2(shapeWidth, m_ShapeHeight);
                        float offset = GetAreaLightOffsetForShadows(shapeSize, areaLightShadowCone);
                        Vector3 shadowOffset = offset * visibleLight.GetForward();
                        HDShadowUtils.ExtractAreaLightData(hdCamera, visibleLight, lightTypeExtent, visibleLight.GetPosition() + shadowOffset, areaLightShadowCone, shadowNearPlane, shapeSize, viewportSize, normalBias, out shadowRequest.view, out invViewProjection, out shadowRequest.deviceProjectionYFlip, out shadowRequest.deviceProjection, out shadowRequest.splitData);
                    }
                    else
                    {
                        // Write per light type matrices, splitDatas and culling parameters
                        switch (legacyLight.type)
                        {
                            case LightType.Point:
                                HDShadowUtils.ExtractPointLightData(
                                    hdCamera, legacyLight.type, visibleLight, viewportSize, shadowNearPlane,
                                    normalBias, (uint)index, out shadowRequest.view,
                                    out invViewProjection, out shadowRequest.deviceProjectionYFlip,
                                    out shadowRequest.deviceProjection, out shadowRequest.splitData
                                );
                            	shadowRequest.constantBias = Math.Max(0.0003f, 10.0f * constantBias / (legacyLight.range * viewportSize.x));
                                break;
                            case LightType.Spot:
                                float spotAngleForShadows = useCustomSpotLightShadowCone ? Math.Min(customSpotLightShadowCone, visibleLight.light.spotAngle)  : visibleLight.light.spotAngle;
                                HDShadowUtils.ExtractSpotLightData(
                                    hdCamera, legacyLight.type, spotLightShape, spotAngleForShadows, shadowNearPlane, aspectRatio, shapeWidth,
                                    shapeHeight, visibleLight, viewportSize, normalBias,
                                    out shadowRequest.view, out invViewProjection, out shadowRequest.deviceProjectionYFlip,
                                    out shadowRequest.deviceProjection, out shadowRequest.splitData
                                );
                            	shadowRequest.constantBias = Math.Max(0.0003f, 20.0f * constantBias / (legacyLight.range * viewportSize.x));
                                break;
                            case LightType.Directional:
                                UpdateDirectionalShadowRequest(manager, visibleLight, cullResults, viewportSize, index, lightIndex, cameraPos, shadowRequest, out invViewProjection);
                                break;
                        }
                    }


                    // Assign all setting common to every lights
                    SetCommonShadowRequestSettings(shadowRequest, cameraPos, invViewProjection, shadowRequest.deviceProjectionYFlip * shadowRequest.view, viewportSize, lightIndex);
                }

                shadowRequest.atlasViewport = resolutionRequest.atlasViewport;
                manager.UpdateShadowRequest(shadowRequestIndex, shadowRequest);
                shadowRequest.shouldUseCachedShadow = shadowRequest.shouldUseCachedShadow && cachedDataIsValid;

                m_CachedDataIsValid = manager.CachedDataIsValid(shadowMapType);
                m_AtlasShapeID = manager.GetAtlasShapeID(shadowMapType);

                // Store the first shadow request id to return it
                if (firstShadowRequestIndex == -1)
                    firstShadowRequestIndex = shadowRequestIndex;

                shadowRequestCount++;
            }

            return firstShadowRequestIndex;
        }

        void SetCommonShadowRequestSettings(HDShadowRequest shadowRequest, Vector3 cameraPos, Matrix4x4 invViewProjection, Matrix4x4 viewProjection, Vector2 viewportSize, int lightIndex)
        {
            // zBuffer param to reconstruct depth position (for transmission)
            float f = legacyLight.range;
            float n = shadowNearPlane;
            shadowRequest.zBufferParam = new Vector4((f-n)/n, 1.0f, (f-n)/n*f, 1.0f/f);
            shadowRequest.worldTexelSize = 2.0f / shadowRequest.deviceProjectionYFlip.m00 / viewportSize.x * Mathf.Sqrt(2.0f);
            shadowRequest.normalBias = normalBias;

            // Make light position camera relative:
            // TODO: think about VR (use different camera position for each eye)
            if (ShaderConfig.s_CameraRelativeRendering != 0)
            {
                var translation = Matrix4x4.Translate(cameraPos);
                shadowRequest.view *= translation;
                translation.SetColumn(3, -cameraPos);
                translation[15] = 1.0f;
                invViewProjection = translation * invViewProjection;
            }

            if (legacyLight.type == LightType.Directional || (legacyLight.type == LightType.Spot && spotLightShape == SpotLightShape.Box))
                shadowRequest.position = new Vector3(shadowRequest.view.m03, shadowRequest.view.m13, shadowRequest.view.m23);
            else
                shadowRequest.position = (ShaderConfig.s_CameraRelativeRendering != 0) ? transform.position - cameraPos : transform.position;

            shadowRequest.shadowToWorld = invViewProjection.transpose;
            shadowRequest.zClip = (legacyLight.type != LightType.Directional);
            shadowRequest.lightIndex = lightIndex;
            // We don't allow shadow resize for directional cascade shadow
            if (legacyLight.type == LightType.Directional)
            {
                shadowRequest.shadowMapType = ShadowMapType.CascadedDirectional;
            }
            else if (lightTypeExtent == LightTypeExtent.Rectangle)
            {
                shadowRequest.shadowMapType = ShadowMapType.AreaLightAtlas;
            }
            else
            {
                shadowRequest.shadowMapType = ShadowMapType.PunctualAtlas;
            }

            shadowRequest.lightType = (int) legacyLight.type;

            // shadow clip planes (used for tessellation clipping)
            GeometryUtility.CalculateFrustumPlanes(viewProjection, m_ShadowFrustumPlanes);
            if (shadowRequest.frustumPlanes?.Length != 6)
                shadowRequest.frustumPlanes = new Vector4[6];
            // Left, right, top, bottom, near, far.
            for (int i = 0; i < 6; i++)
            {
                shadowRequest.frustumPlanes[i] = new Vector4(
                    m_ShadowFrustumPlanes[i].normal.x,
                    m_ShadowFrustumPlanes[i].normal.y,
                    m_ShadowFrustumPlanes[i].normal.z,
                    m_ShadowFrustumPlanes[i].distance
                );
            }

            // Shadow algorithm parameters
            shadowRequest.shadowSoftness = shadowSoftness / 100f;
            shadowRequest.blockerSampleCount = blockerSampleCount;
            shadowRequest.filterSampleCount = filterSampleCount;
            shadowRequest.minFilterSize = minFilterSize;

            shadowRequest.kernelSize = (uint)kernelSize;
            shadowRequest.lightAngle = (lightAngle * Mathf.PI / 180.0f);
            shadowRequest.maxDepthBias = maxDepthBias;
            // We transform it to base two for faster computation.
            // So e^x = 2^y where y = x * log2 (e)
            const float log2e = 1.44269504089f;
            shadowRequest.evsmParams.x = evsmExponent * log2e;
            shadowRequest.evsmParams.y = evsmLightLeakBias;
            shadowRequest.evsmParams.z = m_EvsmVarianceBias;
            shadowRequest.evsmParams.w = evsmBlurPasses;
        }

        // We need these old states to make timeline and the animator record the intensity value and the emissive mesh changes
        [System.NonSerialized]
        TimelineWorkaround timelineWorkaround = new TimelineWorkaround();

#if UNITY_EDITOR

        // Force to retrieve color light's m_UseColorTemperature because it's private
        [System.NonSerialized]
        SerializedProperty m_UseColorTemperatureProperty;
        SerializedProperty useColorTemperatureProperty
        {
            get
            {
                if (m_UseColorTemperatureProperty == null)
                {
                    m_UseColorTemperatureProperty = lightSerializedObject.FindProperty("m_UseColorTemperature");
                }

                return m_UseColorTemperatureProperty;
            }
        }

        [System.NonSerialized]
        SerializedObject m_LightSerializedObject;
        SerializedObject lightSerializedObject
        {
            get
            {
                if (m_LightSerializedObject == null)
                {
                    m_LightSerializedObject = new SerializedObject(legacyLight);
                }

                return m_LightSerializedObject;
            }
        }

#endif

        internal bool useColorTemperature
        {
            get => legacyLight.useColorTemperature;
            set
            {
                if (legacyLight.useColorTemperature == value)
                    return;

                legacyLight.useColorTemperature = value;
            }
        }

        // TODO: we might be able to get rid to that
        [System.NonSerialized]
        bool m_Animated;

        private void Start()
        {
            // If there is an animator attached ot the light, we assume that some of the light properties
            // might be driven by this animator (using timeline or animations) so we force the LateUpdate
            // to sync the animated HDAdditionalLightData properties with the light component.
            m_Animated = GetComponent<Animator>() != null;
        }

        // TODO: There are a lot of old != current checks and assignation in this function, maybe think about using another system ?
        void LateUpdate()
        {
// We force the animation in the editor and in play mode when there is an animator component attached to the light
#if !UNITY_EDITOR
            if (!m_Animated)
                return;
#endif

            Vector3 shape = new Vector3(shapeWidth, m_ShapeHeight, shapeRadius);

            // Check if the intensity have been changed by the inspector or an animator
            if (timelineWorkaround.oldLossyScale != transform.lossyScale
                || intensity != timelineWorkaround.oldIntensity
                || legacyLight.colorTemperature != timelineWorkaround.oldLightColorTemperature)
            {
                UpdateLightIntensity();
                UpdateAreaLightEmissiveMesh();
                timelineWorkaround.oldLossyScale = transform.lossyScale;
                timelineWorkaround.oldIntensity = intensity;
                timelineWorkaround.oldLightColorTemperature = legacyLight.colorTemperature;
            }

            // Same check for light angle to update intensity using spot angle
            if (legacyLight.type == LightType.Spot && (timelineWorkaround.oldSpotAngle != legacyLight.spotAngle))
            {
                UpdateLightIntensity();
                timelineWorkaround.oldSpotAngle = legacyLight.spotAngle;
            }

            if (legacyLight.color != timelineWorkaround.oldLightColor
                || timelineWorkaround.oldLossyScale != transform.lossyScale
                || displayAreaLightEmissiveMesh != timelineWorkaround.oldDisplayAreaLightEmissiveMesh
                || legacyLight.colorTemperature != timelineWorkaround.oldLightColorTemperature)
            {
                UpdateAreaLightEmissiveMesh();
                timelineWorkaround.oldLightColor = legacyLight.color;
                timelineWorkaround.oldLossyScale = transform.lossyScale;
                timelineWorkaround.oldDisplayAreaLightEmissiveMesh = displayAreaLightEmissiveMesh;
                timelineWorkaround.oldLightColorTemperature = legacyLight.colorTemperature;
            }
        }

        void OnDidApplyAnimationProperties()
        {
            UpdateAllLightValues();
        }

        /// <summary>
        /// Copy all field from this to an additional light data
        /// </summary>
        /// <param name="data">Destination component</param>
        public void CopyTo(HDAdditionalLightData data)
        {
#pragma warning disable 618
            data.directionalIntensity = directionalIntensity;
            data.punctualIntensity = punctualIntensity;
            data.areaIntensity = areaIntensity;
#pragma warning restore 618
            data.enableSpotReflector = enableSpotReflector;
            data.luxAtDistance = luxAtDistance;
            data.m_InnerSpotPercent = m_InnerSpotPercent;
            data.lightDimmer = lightDimmer;
            data.volumetricDimmer = volumetricDimmer;
            data.lightUnit = lightUnit;
            data.m_FadeDistance = m_FadeDistance;
            data.affectDiffuse = affectDiffuse;
            data.m_AffectSpecular = m_AffectSpecular;
            data.nonLightmappedOnly = nonLightmappedOnly;
            data.lightTypeExtent = lightTypeExtent;
            data.spotLightShape = spotLightShape;
            data.shapeWidth = shapeWidth;
            data.m_ShapeHeight = m_ShapeHeight;
            data.aspectRatio = aspectRatio;
            data.shapeRadius = shapeRadius;
            data.m_MaxSmoothness = maxSmoothness;
            data.m_ApplyRangeAttenuation = m_ApplyRangeAttenuation;
            data.useOldInspector = useOldInspector;
            data.featuresFoldout = featuresFoldout;
            data.showAdditionalSettings = showAdditionalSettings;
            data.m_Intensity = m_Intensity;
            data.displayAreaLightEmissiveMesh = displayAreaLightEmissiveMesh;
            data.interactsWithSky = interactsWithSky;
            data.angularDiameter = angularDiameter;
            data.distance = distance;

            data.customResolution = customResolution;
            data.shadowDimmer = shadowDimmer;
            data.volumetricShadowDimmer = volumetricShadowDimmer;
            data.shadowFadeDistance = shadowFadeDistance;
            data.contactShadows = contactShadows;
            data.constantBias = constantBias;
            data.normalBias = normalBias;
            data.shadowCascadeRatios = new float[shadowCascadeRatios.Length];
            shadowCascadeRatios.CopyTo(data.shadowCascadeRatios, 0);
            data.shadowCascadeBorders = new float[shadowCascadeBorders.Length];
            shadowCascadeBorders.CopyTo(data.shadowCascadeBorders, 0);
            data.shadowAlgorithm = shadowAlgorithm;
            data.shadowVariant = shadowVariant;
            data.shadowPrecision = shadowPrecision;
            data.shadowUpdateMode = shadowUpdateMode;

            data.m_UseCustomSpotLightShadowCone = useCustomSpotLightShadowCone;
            data.m_CustomSpotLightShadowCone = customSpotLightShadowCone;

#if UNITY_EDITOR
            data.timelineWorkaround = timelineWorkaround;
#endif
        }

        // As we have our own default value, we need to initialize the light intensity correctly
        /// <summary>
        /// Initialize an HDAdditionalLightData that have just beeing created.
        /// </summary>
        /// <param name="lightData"></param>
        public static void InitDefaultHDAdditionalLightData(HDAdditionalLightData lightData)
        {
            // Special treatment for Unity built-in area light. Change it to our rectangle light
            var light = lightData.gameObject.GetComponent<Light>();

            // Set light intensity and unit using its type
            switch (light.type)
            {
                case LightType.Directional:
                    lightData.lightUnit = LightUnit.Lux;
                    lightData.intensity = k_DefaultDirectionalLightIntensity;
                    break;
                case LightType.Rectangle: // Rectangle by default when light is created
                    lightData.lightUnit = LightUnit.Lumen;
                    lightData.intensity = k_DefaultAreaLightIntensity;
                    light.shadows = LightShadows.None;
                    break;
                case LightType.Point:
                case LightType.Spot:
                    lightData.lightUnit = LightUnit.Lumen;
                    lightData.intensity = k_DefaultPunctualLightIntensity;
                    break;
            }

            // Sanity check: lightData.lightTypeExtent is init to LightTypeExtent.Punctual (in case for unknown  reasons we recreate additional data on an existing line)
            if (light.type == LightType.Rectangle && lightData.lightTypeExtent == LightTypeExtent.Punctual)
            {
                lightData.lightTypeExtent = LightTypeExtent.Rectangle;
                light.type = LightType.Point; // Same as in HDLightEditor
#if UNITY_EDITOR
                light.lightmapBakeType = LightmapBakeType.Realtime;
#endif
            }

            // We don't use the global settings of shadow mask by default
            light.lightShadowCasterMode = LightShadowCasterMode.Everything;

            lightData.constantBias         = 0.15f;
            lightData.normalBias           = 0.75f;
        }

        void OnValidate()
        {
            UpdateBounds();
        }

#region Update functions to patch values in the Light component when we change properties inside HDAdditionalLightData

        void SetLightIntensityPunctual(float intensity)
        {
            switch (legacyLight.type)
            {
                case LightType.Directional:
                    legacyLight.intensity = intensity; // Always in lux
                    break;
                case LightType.Point:
                    if (lightUnit == LightUnit.Candela)
                        legacyLight.intensity = intensity;
                    else
                        legacyLight.intensity = LightUtils.ConvertPointLightLumenToCandela(intensity);
                    break;
                case LightType.Spot:
                    if (lightUnit == LightUnit.Candela)
                    {
                        // When using candela, reflector don't have any effect. Our intensity is candela = lumens/steradian and the user
                        // provide desired value for an angle of 1 steradian.
                        legacyLight.intensity = intensity;
                    }
                    else  // lumen
                    {
                        if (enableSpotReflector)
                        {
                            // If reflector is enabled all the lighting from the sphere is focus inside the solid angle of current shape
                            if (spotLightShape == SpotLightShape.Cone)
                            {
                                legacyLight.intensity = LightUtils.ConvertSpotLightLumenToCandela(intensity, legacyLight.spotAngle * Mathf.Deg2Rad, true);
                            }
                            else if (spotLightShape == SpotLightShape.Pyramid)
                            {
                                float angleA, angleB;
                                LightUtils.CalculateAnglesForPyramid(aspectRatio, legacyLight.spotAngle * Mathf.Deg2Rad, out angleA, out angleB);

                                legacyLight.intensity = LightUtils.ConvertFrustrumLightLumenToCandela(intensity, angleA, angleB);
                            }
                            else // Box shape, fallback to punctual light.
                            {
                                legacyLight.intensity = LightUtils.ConvertPointLightLumenToCandela(intensity);
                            }
                        }
                        else
                        {
                            // No reflector, angle act as occlusion of point light.
                            legacyLight.intensity = LightUtils.ConvertPointLightLumenToCandela(intensity);
                        }
                    }
                    break;
            }
        }

        void UpdateLightIntensity()
        {
            if (lightUnit == LightUnit.Lumen)
            {
                if (lightTypeExtent == LightTypeExtent.Punctual)
                    SetLightIntensityPunctual(intensity);
                else
                    legacyLight.intensity = LightUtils.ConvertAreaLightLumenToLuminance(lightTypeExtent, intensity, shapeWidth, m_ShapeHeight);
            }
            else if (lightUnit == LightUnit.Ev100)
            {
                legacyLight.intensity = LightUtils.ConvertEvToLuminance(m_Intensity);
            }
            else if ((legacyLight.type == LightType.Spot || legacyLight.type == LightType.Point) && lightUnit == LightUnit.Lux)
            {
                // Box are local directional light with lux unity without at distance
                if ((legacyLight.type == LightType.Spot) && (spotLightShape == SpotLightShape.Box))
                    legacyLight.intensity = m_Intensity;
                else
                    legacyLight.intensity = LightUtils.ConvertLuxToCandela(m_Intensity, luxAtDistance);
            }
            else
                legacyLight.intensity = m_Intensity;

#if UNITY_EDITOR
            legacyLight.SetLightDirty(); // Should be apply only to parameter that's affect GI, but make the code cleaner
#endif
        }

        internal void UpdateAreaLightEmissiveMesh()
        {
            MeshRenderer emissiveMeshRenderer = GetComponent<MeshRenderer>();
            MeshFilter emissiveMeshFilter = GetComponent<MeshFilter>();

            bool displayEmissiveMesh = IsAreaLight(lightTypeExtent) && displayAreaLightEmissiveMesh;

            // Ensure that the emissive mesh components are here
            if (displayEmissiveMesh)
            {
                if (emissiveMeshRenderer == null)
                    emissiveMeshRenderer = gameObject.AddComponent<MeshRenderer>();
                if (emissiveMeshFilter == null)
                    emissiveMeshFilter = gameObject.AddComponent<MeshFilter>();
            }
            else // Or remove them if the option is disabled
            {
                if (emissiveMeshRenderer != null)
                    CoreUtils.Destroy(emissiveMeshRenderer);
                if (emissiveMeshFilter != null)
                    CoreUtils.Destroy(emissiveMeshFilter);

                // We don't have anything to do left if the dislay emissive mesh option is disabled
                return;
            }

            Vector3 lightSize;

            // Update light area size from GameObject transform scale if the transform have changed
            // else we update the light size from the shape fields
            if (timelineWorkaround.oldLossyScale != transform.lossyScale)
                lightSize = transform.lossyScale;
            else
                lightSize = new Vector3(m_ShapeWidth, m_ShapeHeight, transform.localScale.z);

            if (lightTypeExtent == LightTypeExtent.Tube)
                lightSize.y = k_MinAreaWidth;
            lightSize.z = k_MinAreaWidth;

            lightSize = Vector3.Max(Vector3.one * k_MinAreaWidth, lightSize);
#if UNITY_EDITOR
            legacyLight.areaSize = lightSize;

            // When we're inside the editor, and the scale of the transform will change
            // then we must record it with inside the undo
            if (legacyLight.transform.localScale != lightSize)
            {
                Undo.RecordObject(transform, "Light Scale changed");
            }
#endif

            Vector3 lossyToLocalScale = lightSize;
            if (transform.parent != null)
            {
                lossyToLocalScale = new Vector3(
                    lightSize.x / transform.parent.lossyScale.x,
                    lightSize.y / transform.parent.lossyScale.y,
                    lightSize.z / transform.parent.lossyScale.z
                );
            }
            legacyLight.transform.localScale = lossyToLocalScale;

            switch (lightTypeExtent)
            {
                case LightTypeExtent.Rectangle:
                    m_ShapeWidth = lightSize.x;
                    m_ShapeHeight = lightSize.y;
                    break;
                case LightTypeExtent.Tube:
                    m_ShapeWidth = lightSize.x;
                    break;
                default:
                    break;
            }

            // NOTE: When the user duplicates a light in the editor, the material is not duplicated and when changing the properties of one of them (source or duplication)
            // It either overrides both or is overriden. Given that when we duplicate an object the name changes, this approach works. When the name of the game object is then changed again
            // the material is not re-created until one of the light properties is changed again.
            if (emissiveMeshRenderer.sharedMaterial == null || emissiveMeshRenderer.sharedMaterial.name != gameObject.name)
            {
                emissiveMeshRenderer.sharedMaterial = new Material(Shader.Find("HDRP/Unlit"));
                emissiveMeshRenderer.sharedMaterial.SetFloat("_IncludeIndirectLighting", 0.0f);
                emissiveMeshRenderer.sharedMaterial.name = gameObject.name;
            }

            // Update Mesh emissive properties
            emissiveMeshRenderer.sharedMaterial.SetColor("_UnlitColor", Color.black);

            // m_Light.intensity is in luminance which is the value we need for emissive color
            Color value = legacyLight.color.linear * legacyLight.intensity;

// We don't have access to the color temperature in the player because it's a private member of the Light component
#if UNITY_EDITOR
            if (useColorTemperature)
                value *= Mathf.CorrelatedColorTemperatureToRGB(legacyLight.colorTemperature);
#endif

            value *= lightDimmer;

            emissiveMeshRenderer.sharedMaterial.SetColor("_EmissiveColor", value);

            // Set the cookie (if there is one) and raise or remove the shader feature
            emissiveMeshRenderer.sharedMaterial.SetTexture("_EmissiveColorMap", areaLightCookie);
            CoreUtils.SetKeyword(emissiveMeshRenderer.sharedMaterial, "_EMISSIVE_COLOR_MAP", areaLightCookie != null);
        }

        void UpdateAreaLightBounds()
        {
            legacyLight.useShadowMatrixOverride = false;
            legacyLight.useBoundingSphereOverride = true;
            legacyLight.boundingSphereOverride = new Vector4(0.0f, 0.0f, 0.0f, legacyLight.range);
        }

        void UpdateBoxLightBounds()
        {
            legacyLight.useShadowMatrixOverride = true;
            legacyLight.useBoundingSphereOverride = true;

            // Need to inverse scale because culling != rendering convention apparently
            Matrix4x4 scaleMatrix = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));
            legacyLight.shadowMatrixOverride = HDShadowUtils.ExtractBoxLightProjectionMatrix(legacyLight.range, shapeWidth, m_ShapeHeight, shadowNearPlane) * scaleMatrix;

            // Very conservative bounding sphere taking the diagonal of the shape as the radius
            float diag = new Vector3(shapeWidth * 0.5f, m_ShapeHeight * 0.5f, legacyLight.range * 0.5f).magnitude;
            legacyLight.boundingSphereOverride = new Vector4(0.0f, 0.0f, legacyLight.range * 0.5f, diag);
        }

        void UpdatePyramidLightBounds()
        {
            legacyLight.useShadowMatrixOverride = true;
            legacyLight.useBoundingSphereOverride = true;

            // Need to inverse scale because culling != rendering convention apparently
            Matrix4x4 scaleMatrix = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));
            legacyLight.shadowMatrixOverride = HDShadowUtils.ExtractSpotLightProjectionMatrix(legacyLight.range, legacyLight.spotAngle, shadowNearPlane, aspectRatio, 0.0f) * scaleMatrix;

            // Very conservative bounding sphere taking the diagonal of the shape as the radius
            float diag = new Vector3(shapeWidth * 0.5f, m_ShapeHeight * 0.5f, legacyLight.range * 0.5f).magnitude;
            legacyLight.boundingSphereOverride = new Vector4(0.0f, 0.0f, legacyLight.range * 0.5f, diag);
        }

        void UpdateBounds()
        {
            if (lightTypeExtent == LightTypeExtent.Punctual && legacyLight.type == LightType.Spot)
            {
                switch (spotLightShape)
                {
                    case SpotLightShape.Box:
                        UpdateBoxLightBounds();
                        break;
                    case SpotLightShape.Pyramid:
                        UpdatePyramidLightBounds();
                        break;
                    default: // Cone
                        legacyLight.useBoundingSphereOverride = false;
                        legacyLight.useShadowMatrixOverride = false;
                        break;
                }
            }
            else if (lightTypeExtent == LightTypeExtent.Rectangle || lightTypeExtent == LightTypeExtent.Tube)
            {
                UpdateAreaLightBounds();
            }
            else
            {
                legacyLight.useBoundingSphereOverride = false;
                legacyLight.useShadowMatrixOverride = false;
            }
        }

        void UpdateShapeSize()
        {
            // Force to clamp the shape if we changed the type of the light
            shapeWidth = m_ShapeWidth;
            shapeHeight = m_ShapeHeight;
        }

        /// <summary>
        /// Synchronize all the HD Additional Light values with the Light component.
        /// </summary>
        public void UpdateAllLightValues()
        {
            UpdateShapeSize();

            // Update light intensity
            UpdateLightIntensity();

            // Patch bounds
            UpdateBounds();

            UpdateAreaLightEmissiveMesh();
            // TODO: synch emissive quad
        }

#endregion

#region User API functions

        /// <summary>
        /// Set the color of the light.
        /// </summary>
        /// <param name="color">Color</param>
        /// <param name="colorTemperature">Optional color temperature</param>
        public void SetColor(Color color, float colorTemperature = -1)
        {
            if (colorTemperature != -1)
            {
                legacyLight.colorTemperature = colorTemperature;
                useColorTemperature = true;
            }

            this.color = color;
        }

        /// <summary>
        /// Toggle the usage of color temperature.
        /// </summary>
        /// <param name="hdLight"></param>
        /// <param name="enable"></param>
        public void EnableColorTemperature(bool enable)
        {
            useColorTemperature = enable;
        }

        /// <summary>
        /// Set the intensity of the light using the current unit.
        /// </summary>
        /// <param name="intensity"></param>
        public void SetIntensity(float intensity) => this.intensity = intensity;

        /// <summary>
        /// Set the intensity of the light using unit in parameter.
        /// </summary>
        /// <param name="intensity"></param>
        /// <param name="unit">Unit must be a valid Light Unit for the current light type</param>
        public void SetIntensity(float intensity, LightUnit unit)
        {
            this.lightUnit = unit;
            this.intensity = intensity;
        }

        /// <summary>
        /// For Spot Lights only, set the intensity that the spot should emit at a certain distance in meter
        /// </summary>
        /// <param name="luxIntensity"></param>
        /// <param name="distance"></param>
        public void SetSpotLightLuxAt(float luxIntensity, float distance)
        {
            lightUnit = LightUnit.Lux;
            luxAtDistance = distance;
            intensity = luxIntensity;
        }

        /// <summary>
        /// Set the range of the light.
        /// </summary>
        /// <param name="range"></param>
        public void SetRange(float range) => legacyLight.range = range;

        /// <summary>
        /// Set the type of the light.
        /// Note: this will also change the unit of the light if the current one is not supported by the new light type.
        /// </summary>
        /// <param name="type"></param>
        public void SetLightType(HDLightType type)
        {
            switch (type)
            {
                case HDLightType.BoxSpot:
                    legacyLight.type = LightType.Spot;
                    spotLightShape = SpotLightShape.Box;
                    lightTypeExtent = LightTypeExtent.Punctual;
                    break;
                case HDLightType.PyramidSpot:
                    legacyLight.type = LightType.Spot;
                    spotLightShape = SpotLightShape.Pyramid;
                    lightTypeExtent = LightTypeExtent.Punctual;
                    break;
                case HDLightType.ConeSpot:
                    legacyLight.type = LightType.Spot;
                    spotLightShape = SpotLightShape.Cone;
                    lightTypeExtent = LightTypeExtent.Punctual;
                    break;
                case HDLightType.Directional:
                    legacyLight.type = LightType.Directional;
                    lightTypeExtent = LightTypeExtent.Punctual;
                    break;
                case HDLightType.Rectangle:
                    legacyLight.type = LightType.Point;
                    lightTypeExtent = LightTypeExtent.Rectangle;
                    break;
                case HDLightType.Tube:
                    legacyLight.type = LightType.Point;
                    lightTypeExtent = LightTypeExtent.Tube;
                    break;
                case HDLightType.Point:
                    legacyLight.type = LightType.Point;
                    lightTypeExtent = LightTypeExtent.Punctual;
                    break;
            }
        }

        /// <summary>
        /// Get the HD light type.
        /// </summary>
        /// <returns></returns>
        public HDLightType GetLightType()
        {
            if (lightTypeExtent == LightTypeExtent.Rectangle)
                return HDLightType.Rectangle;
            else if (lightTypeExtent == LightTypeExtent.Tube)
                return HDLightType.Tube;
            else
            {
                switch (legacyLight.type)
                {
                    case LightType.Spot:
                        switch (spotLightShape)
                        {
                            case SpotLightShape.Box: return HDLightType.BoxSpot;
                            case SpotLightShape.Pyramid: return HDLightType.PyramidSpot;
                            default:
                            case SpotLightShape.Cone: return HDLightType.ConeSpot;
                        }
                    case LightType.Directional: return HDLightType.Directional;
                    default:
                    case LightType.Point: return HDLightType.Point;
                }
            }
        }

        /// <summary>
        /// Set light cookie.
        /// </summary>
        /// <param name="cookie2D">Cookie texture, must be 2D for Directional, Spot and Area light and Cubemap for Point lights</param>
        /// <param name="directionalLightCookieSize">area light </param>
        public void SetCookie(Texture cookie, Vector2 directionalLightCookieSize)
        {
            if (IsAreaLight(lightTypeExtent))
            {
                if (cookie.dimension != TextureDimension.Tex2D)
                {
                    Debug.LogError("Texture dimension " + cookie.dimension + " is not supported for area lights.");
                    return ;
                }
                areaLightCookie = cookie;
            }
            else
            {
                if (legacyLight.type == LightType.Point && cookie.dimension != TextureDimension.Cube)
                {
                    Debug.LogError("Texture dimension " + cookie.dimension + " is not supported for point lights.");
                    return ;
                }
                else if (legacyLight.type != LightType.Point && cookie.dimension != TextureDimension.Tex2D) // Only 2D cookie are supported for Directional and Spot lights
                {
                    Debug.LogError("Texture dimension " + cookie.dimension + " is not supported for Directional/Spot lights.");
                    return ;
                }
                if (legacyLight.type == LightType.Directional)
                {
                    shapeWidth = directionalLightCookieSize.x;
                    shapeHeight = directionalLightCookieSize.y;
                }
                legacyLight.cookie = cookie;
            }
        }

        /// <summary>
        /// Set light cookie.
        /// </summary>
        /// <param name="cookie2D">Cookie texture, must be 2D for Directional, Spot and Area light and Cubemap for Point lights</param>
        public void SetCookie(Texture cookie) => SetCookie(cookie, Vector2.zero);

        /// <summary>
        /// Set the spot light angle and inner spot percent. We don't use Light.innerSpotAngle.
        /// </summary>
        /// <param name="angle">inner spot angle in degree</param>
        /// <param name="innerSpotPercent">inner spot angle in percent</param>
        public void SetSpotAngle(float angle, float innerSpotPercent = 0)
        {
            this.legacyLight.spotAngle = angle;
            this.innerSpotPercent = innerSpotPercent;
        }

        /// <summary>
        /// Set the dimmer for light and volumetric light.
        /// </summary>
        /// <param name="dimmer"></param>
        /// <param name="volumetricLightDimmer"></param>
        public void SetLightDimmer(float dimmer = 1, float volumetricDimmer = 1)
        {
            this.lightDimmer = dimmer;
            this.volumetricDimmer = volumetricDimmer;
        }

        /// <summary>
        /// Set the light unit.
        /// </summary>
        /// <param name="unit"></param>
        public void SetLightUnit(LightUnit unit) => lightUnit = unit;

        /// <summary>
        /// Enable shadows on a light.
        /// </summary>
        /// <param name="enabled"></param>
        public void EnableShadows(bool enabled) => legacyLight.shadows = enabled ? LightShadows.Soft : LightShadows.None;

        /// <summary>
        /// Set the shadow resolution.
        /// </summary>
        /// <param name="resolution">Must be between 16 and 16384</param>
        public void SetShadowResolution(int resolution) => customResolution = resolution;

        /// <summary>
        /// Set the near plane of the shadow.
        /// </summary>
        /// <param name="nearPlaneDistance"></param>
        public void SetShadowNearPlane(float nearPlaneDistance) => shadowNearPlane = nearPlaneDistance;

        /// <summary>
        /// Set parameters for PCSS shadows.
        /// </summary>
        /// <param name="softness">How soft the shadow will be, between 0 and 1</param>
        /// <param name="blockerSampleCount">Number of samples used to detect blockers</param>
        /// <param name="filterSampleCount">Number of samples used to filter the shadow map</param>
        /// <param name="minFilterSize">Minimum filter size</param>
        public void SetPCSSParams(float softness, int blockerSampleCount = 16, int filterSampleCount = 24, float minFilterSize = 0.00001f)
        {
            this.shadowSoftness = softness;
            this.blockerSampleCount = blockerSampleCount;
            this.filterSampleCount = filterSampleCount;
            this.minFilterSize = minFilterSize;
        }

        /// <summary>
        /// Set the light layer and shadow map light layer masks. The feature must be enabled in the HDRP asset in norder to work.
        /// </summary>
        /// <param name="lightLayerMask"></param>
        /// <param name="shadowLightLayerMask"></param>
        public void SetLightLayer(LightLayerEnum lightLayerMask, LightLayerEnum shadowLightLayerMask)
        {
            // disable the shadow / light layer link
            linkShadowLayers = false;
            legacyLight.renderingLayerMask = LightLayerToRenderingLayerMask((int)lightLayerMask, (int)legacyLight.renderingLayerMask);
            lightlayersMask = shadowLightLayerMask;
        }

        /// <summary>
        /// Set the shadow dimmer.
        /// </summary>
        /// <param name="shadowDimmer">Dimmer between 0 and 1</param>
        /// <param name="volumetricShadowDimmer">Dimmer between 0 and 1 for volumetrics</param>
        public void SetShadowDimmer(float shadowDimmer = 1, float volumetricShadowDimmer = 1)
        {
            this.shadowDimmer = shadowDimmer;
            this.volumetricShadowDimmer = volumetricShadowDimmer;
        }

        /// <summary>
        /// Shadow fade distance in meter.
        /// </summary>
        /// <param name="distance"></param>
        public void SetShadowFadeDistance(float distance) => shadowFadeDistance = distance;

        /// <summary>
        /// Enable/Disable the contact shadows, the feature must be enable in the HDRP asset to work.
        /// </summary>
        /// <param name="enabled"></param>
        public void EnableContactShadows(bool enabled) => contactShadows = enabled;

        /// <summary>
        /// Set the Shadow tint for the directional light.
        /// </summary>
        /// <param name="tint"></param>
        public void SetDirectionalShadowTint(Color tint) => shadowTint = tint;

        /// <summary>
        /// Set the shadow update mode.
        /// </summary>
        /// <param name="updateMode"></param>
        public void SetShadowUpdateMode(ShadowUpdateMode updateMode) => shadowUpdateMode = updateMode;

        // A bunch of function that changes stuff on the legacy light so users don't have to get the
        // light component which would lead to synchronization problem with ou HD datas.

        /// <summary>
        /// Set the light layer and shadow map light layer masks. The feature must be enabled in the HDRP asset in norder to work.
        /// </summary>
        /// <param name="lightLayerMask"></param>
        public void SetLightLayer(LightLayerEnum lightLayerMask) => legacyLight.renderingLayerMask = (int)lightLayerMask;

        /// <summary>
        /// Set the light culling mask.
        /// </summary>
        /// <param name="cullingMask"></param>
        public void SetCullingMask(int cullingMask) => legacyLight.cullingMask = cullingMask;

        /// <summary>
        /// Set the light layer shadow cull distances.
        /// </summary>
        /// <param name="layerShadowCullDistances"></param>
        /// <returns></returns>
        public float[] SetLayerShadowCullDistances(float[] layerShadowCullDistances) => legacyLight.layerShadowCullDistances = layerShadowCullDistances;

        /// <summary>
        /// Get the list of supported light units depending on the current light type.
        /// </summary>
        /// <returns></returns>
        public LightUnit[] GetSupportedLightUnits() => GetSupportedLightUnits(legacyLight.type, lightTypeExtent);

        /// <summary>
        /// Set the area light size.
        /// </summary>
        /// <param name="size"></param>
        public void SetAreaLightSize(Vector2 size)
        {
            if (IsAreaLight(lightTypeExtent))
            {
                m_ShapeWidth = size.x;
                m_ShapeHeight = size.y;
                UpdateAllLightValues();
            }
        }

        /// <summary>
        /// Set the box spot light size.
        /// </summary>
        /// <param name="size"></param>
        public void SetBoxSpotSize(Vector2 size)
        {
            if (legacyLight.type == LightType.Spot)
            {
                shapeWidth = size.x;
                shapeHeight = size.y;
            }
        }

#endregion

#region Utils

        bool IsValidLightUnitForType(LightType type, LightTypeExtent typeExtent, LightUnit unit)
        {
            LightUnit[] allowedUnits = GetSupportedLightUnits(type, typeExtent);

            return allowedUnits.Any(u => u == unit);
        }

        [System.NonSerialized]
        Dictionary<int, LightUnit[]>  supportedLightTypeCache = new Dictionary<int, LightUnit[]>();
        LightUnit[] GetSupportedLightUnits(LightType type, LightTypeExtent typeExtent)
        {
            LightUnit[]     supportedTypes;

            // Combine the two light types to access the dictionary
            int cacheKey = (int)type | ((int)typeExtent << 16);
            // We cache the result once they are computed, it avoid garbage generated by Enum.GetValues and Linq.
            if (supportedLightTypeCache.TryGetValue(cacheKey, out supportedTypes))
                return supportedTypes;

            if (IsAreaLight(typeExtent))
                supportedTypes = Enum.GetValues(typeof(AreaLightUnit)).Cast<LightUnit>().ToArray();
            else if (type == LightType.Directional || (type == LightType.Spot && spotLightShape == SpotLightShape.Box))
                supportedTypes = Enum.GetValues(typeof(DirectionalLightUnit)).Cast<LightUnit>().ToArray();
            else
                supportedTypes = Enum.GetValues(typeof(PunctualLightUnit)).Cast<LightUnit>().ToArray();

            return supportedTypes;
        }

        string GetLightTypeName()
        {
            if (IsAreaLight(lightTypeExtent))
                return lightTypeExtent.ToString();
            else
                return legacyLight.type.ToString();
        }

        internal static bool IsAreaLight(LightTypeExtent lightType)
        {
            return lightType != LightTypeExtent.Punctual;
        }

#if UNITY_EDITOR
        internal static bool IsAreaLight(SerializedProperty lightType)
        {
            return IsAreaLight((LightTypeExtent)lightType.enumValueIndex);
        }
#endif

#endregion

        /// <summary>
        /// Converts a light layer into a rendering layer mask.
        ///
        /// Light layer is stored in the first 8 bit of the rendering layer mask.
        ///
        /// NOTE: light layers are obsolete, use directly renderingLayerMask.
        /// </summary>
        /// <param name="lightLayer">The light layer, only the first 8 bits will be used.</param>
        /// <param name="renderingLayerMask">Current renderingLayerMask, only the last 24 bits will be used.</param>
        /// <returns></returns>
        internal static int LightLayerToRenderingLayerMask(int lightLayer, int renderingLayerMask)
        {
            var renderingLayerMask_u32 = (uint)renderingLayerMask;
            var lightLayer_u8 = (byte)lightLayer;
            return (int)((renderingLayerMask_u32 & 0xFFFFFF00) | lightLayer_u8);
        }

        /// <summary>
        /// Converts a renderingLayerMask into a lightLayer.
        ///
        /// NOTE: light layers are obsolete, use directly renderingLayerMask.
        /// </summary>
        /// <param name="renderingLayerMask"></param>
        /// <returns></returns>
        internal static int RenderingLayerMaskToLightLayer(int renderingLayerMask)
            => (byte)renderingLayerMask;
    }
}
