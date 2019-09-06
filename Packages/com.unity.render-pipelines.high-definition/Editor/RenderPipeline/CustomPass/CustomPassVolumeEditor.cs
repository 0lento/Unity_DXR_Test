using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEditorInternal;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System;
using System.Reflection;

namespace UnityEditor.Rendering.HighDefinition
{
    [CustomEditor(typeof(CustomPassVolume))]
    sealed class CustomPassVolumeEditor : Editor
    {
        ReorderableList         m_CustomPassList;
        string                  m_ListName;
        CustomPassVolume        m_Volume;

        const string            k_DefaultListName = "Custom Passes";

        static class Styles
        {
            public static readonly GUIContent isGlobal = new GUIContent("Is Global", "Is the volume for the entire scene.");
            public static readonly GUIContent injectionPoint = new GUIContent("Injection Point", "Where the pass is going to be executed in the pipeline.");
        }

        class SerializedPassVolume
        {
            public SerializedProperty   isGlobal;
            public SerializedProperty   customPasses;
            public SerializedProperty   injectionPoint;
        }

        SerializedPassVolume    m_SerializedPassVolume;

        void OnEnable()
        {
            m_Volume = target as CustomPassVolume;

            using (var o = new PropertyFetcher<CustomPassVolume>(serializedObject))
            {
                m_SerializedPassVolume = new SerializedPassVolume
                {
                    isGlobal = o.Find(x => x.isGlobal),
                    injectionPoint = o.Find(x => x.injectionPoint),
                    customPasses = o.Find(x => x.customPasses),
                };
            }
            
            CreateReorderableList(m_SerializedPassVolume.customPasses);
        }

        public override void OnInspectorGUI()
        {
            DrawSettingsGUI();
            DrawCustomPassReorderableList();
        }

        Dictionary<SerializedProperty, CustomPassDrawer> customPassDrawers = new Dictionary<SerializedProperty, CustomPassDrawer>();
        CustomPassDrawer GetCustomPassDrawer(SerializedProperty pass, int listIndex)
        {
            CustomPassDrawer drawer;

            if (customPassDrawers.TryGetValue(pass, out drawer))
                return drawer;

            var passType = m_Volume.customPasses[listIndex].GetType();

            foreach (var drawerType in TypeCache.GetTypesWithAttribute(typeof(CustomPassDrawerAttribute)))
            {
                var attr = drawerType.GetCustomAttributes(typeof(CustomPassDrawerAttribute), true)[0] as CustomPassDrawerAttribute;
                if (attr.targetPassType == passType)
                {
                    drawer = Activator.CreateInstance(drawerType) as CustomPassDrawer;
                    drawer.SetPassType(passType);
                    break;
                }
                if (attr.targetPassType.IsAssignableFrom(passType))
                {
                    drawer = Activator.CreateInstance(drawerType) as CustomPassDrawer;
                    drawer.SetPassType(passType);
                }
            }

            customPassDrawers[pass] = drawer;

            return drawer;
        }

        void DrawSettingsGUI()
        {
            serializedObject.Update();
            
            EditorGUI.BeginChangeCheck();
            {
                EditorGUILayout.PropertyField(m_SerializedPassVolume.isGlobal, Styles.isGlobal);
                EditorGUILayout.PropertyField(m_SerializedPassVolume.injectionPoint, Styles.injectionPoint);
            }
            if (EditorGUI.EndChangeCheck())
                serializedObject.ApplyModifiedProperties();
        }

        void DrawCustomPassReorderableList()
        {
            // Sanitize list:
            for (int i = 0; i < m_SerializedPassVolume.customPasses.arraySize; i++)
            {
                if (m_SerializedPassVolume.customPasses.GetArrayElementAtIndex(i) == null)
                {
                    m_SerializedPassVolume.customPasses.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    i++;
                }
            }

            EditorGUILayout.BeginVertical();
            m_CustomPassList.DoLayoutList();
            EditorGUILayout.EndVertical();
        }

        void CreateReorderableList(SerializedProperty passList)
        {
            m_CustomPassList = new ReorderableList(passList.serializedObject, passList);

            m_CustomPassList.drawHeaderCallback = (rect) => {
                EditorGUI.LabelField(rect, k_DefaultListName, EditorStyles.largeLabel);
            };

            m_CustomPassList.drawElementCallback = (rect, index, active, focused) => {
                EditorGUI.BeginChangeCheck();
                
                var customPass = passList.GetArrayElementAtIndex(index);
                var drawer = GetCustomPassDrawer(customPass, index);
                if (drawer != null)
                    drawer.OnGUI(rect, customPass, null);
                else
                    EditorGUI.PropertyField(rect, customPass);
                if (EditorGUI.EndChangeCheck())
                    customPass.serializedObject.ApplyModifiedProperties();
            };

            m_CustomPassList.elementHeightCallback = (index) =>
            {
                var customPass = passList.GetArrayElementAtIndex(index);
                var drawer = GetCustomPassDrawer(customPass, index);
                if (drawer != null)
                    return drawer.GetPropertyHeight(customPass, null);
                else
                    return EditorGUI.GetPropertyHeight(customPass, null);
            };

            m_CustomPassList.onAddCallback += (list) => {
                Undo.RegisterCompleteObjectUndo(target, "Remove custom pass");

                var menu = new GenericMenu();
                foreach (var customPassType in TypeCache.GetTypesDerivedFrom<CustomPass>())
                    menu.AddItem(new GUIContent(customPassType.Name), false, () => {
                        m_Volume.AddPassOfType(customPassType);
                        passList.serializedObject.Update();
                    });
                menu.ShowAsContext();
			};

            m_CustomPassList.onRemoveCallback = (list) => {
                Undo.RegisterCompleteObjectUndo(target, "Remove custom pass");
                m_Volume.customPasses.RemoveAt(list.index);
                passList.serializedObject.Update();
            };
        }

        float GetCustomPassEditorHeight(SerializedProperty pass) => EditorGUIUtility.singleLineHeight;
    }
}