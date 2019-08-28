using System;

// define ENABLE_BAKED_PLANAR to enable baked planar

namespace UnityEngine.Rendering.HighDefinition
{
    [HelpURL(Documentation.baseURL + Documentation.version + Documentation.subURL + "Planar-Reflection-Probe" + Documentation.endURL)]
    [ExecuteAlways]
    public sealed partial class PlanarReflectionProbe : HDProbe
    {
        // Serialized data
        [SerializeField]
        Vector3 m_LocalReferencePosition = -Vector3.forward;

        /// <summary>Reference position to mirror to find the capture point. (local space)</summary>
        public Vector3 localReferencePosition { get => m_LocalReferencePosition; set => m_LocalReferencePosition = value; }
        /// <summary>Reference position to mirror to find the capture point. (world space)</summary>
        public Vector3 referencePosition => transform.TransformPoint(m_LocalReferencePosition);

        void Awake()
        {
            type = ProbeSettings.ProbeType.PlanarProbe;
            k_PlanarProbeMigration.Migrate(this);
        }
    }
}
