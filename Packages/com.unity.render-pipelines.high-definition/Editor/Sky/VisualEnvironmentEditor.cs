using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor.Rendering;

namespace UnityEditor.Rendering.HighDefinition
{
    [VolumeComponentEditor(typeof(VisualEnvironment))]
    class VisualEnvironmentEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_SkyType;
        SerializedDataParameter m_SkyAmbientMode;
        SerializedDataParameter m_FogType;

        List<GUIContent> m_SkyClassNames = null;
        List<GUIContent> m_FogNames = null;
        List<int> m_SkyUniqueIDs = null;

        public static readonly string[] fogNames = Enum.GetNames(typeof(FogType));
        public static readonly int[] fogValues = Enum.GetValues(typeof(FogType)) as int[];

        public override void OnEnable()
        {
            base.OnEnable();
            var o = new PropertyFetcher<VisualEnvironment>(serializedObject);

            m_SkyType = Unpack(o.Find(x => x.skyType));
            m_SkyAmbientMode = Unpack(o.Find(x => x.skyAmbientMode));
            m_FogType = Unpack(o.Find(x => x.fogType));
        }

        void UpdateSkyAndFogIntPopupData()
        {
            if (m_SkyClassNames == null)
            {
                m_SkyClassNames = new List<GUIContent>();
                m_SkyUniqueIDs = new List<int>();

                // Add special "None" case.
                m_SkyClassNames.Add(new GUIContent("None"));
                m_SkyUniqueIDs.Add(0);

                var skyTypesDict = SkyManager.skyTypesDict;

                foreach (KeyValuePair<int, Type> kvp in skyTypesDict)
                {
                    string name = ObjectNames.NicifyVariableName(kvp.Value.Name.ToString());
                    name = name.Replace("Settings", ""); // remove Settings if it was in the class name
                    m_SkyClassNames.Add(new GUIContent(name));
                    m_SkyUniqueIDs.Add(kvp.Key);
                }
            }

            if (m_FogNames == null)
            {
                m_FogNames = new List<GUIContent>();

                foreach (string fogStr in fogNames)
                {
                    // Add Fog on each members of the enum except for None
                    m_FogNames.Add(new GUIContent(fogStr + (fogStr != "None" ? " Fog" : "")));
                }
            }
        }

        public override void OnInspectorGUI()
        {
            UpdateSkyAndFogIntPopupData();

            EditorGUILayout.LabelField(EditorGUIUtility.TrTextContent("Sky"), EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawOverrideCheckbox(m_SkyType);
                using (new EditorGUI.DisabledScope(!m_SkyType.overrideState.boolValue))
                {
                    EditorGUILayout.IntPopup(m_SkyType.value, m_SkyClassNames.ToArray(), m_SkyUniqueIDs.ToArray(), EditorGUIUtility.TrTextContent("Type", "Specifies the type of sky this Volume uses."));
                }
            }
            if (m_SkyType.value.intValue != 0)
                EditorGUILayout.HelpBox("You need to also add a Volume Component matching the selected type.", MessageType.Info);
            PropertyField(m_SkyAmbientMode, EditorGUIUtility.TrTextContent("Ambient Mode"));

            var staticLightingSky = SkyManager.GetStaticLightingSky();
            if ((SkyAmbientMode)m_SkyAmbientMode.value.enumValueIndex == SkyAmbientMode.Static)
            {
                if (staticLightingSky == null)
                    EditorGUILayout.HelpBox("Current Static Lighting Sky use None of profile None.", MessageType.Info);
                else
                {
                    var skyType = staticLightingSky.staticLightingSkyUniqueID == 0 ? "None" : SkyManager.skyTypesDict[staticLightingSky.staticLightingSkyUniqueID].Name.ToString();
                    EditorGUILayout.HelpBox($"Current Static Lighting Sky use {skyType} of profile {staticLightingSky.profile?.name ?? "None"}.", MessageType.Info);
                }
            }

            EditorGUILayout.LabelField(EditorGUIUtility.TrTextContent("Fog"), EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawOverrideCheckbox(m_FogType);
                using (new EditorGUI.DisabledScope(!m_FogType.overrideState.boolValue))
                {
                    EditorGUILayout.IntPopup(m_FogType.value, m_FogNames.ToArray(), fogValues, EditorGUIUtility.TrTextContent("Type", "Specifies the type of fog this Volume uses."));
                }
            }
        }
    }
}
