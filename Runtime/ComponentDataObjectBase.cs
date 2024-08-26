using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System;

public class ComponentDataObjectBase : ScriptableObject
{
    public string title;

    [TextArea]
    public string description;

    [HideInInspector]
    public string type;

    [ScriptableObjectId]
    public string guid;

    // Add the AssignedAsset property
    public List<UnityEngine.Object> childAssets = new List<UnityEngine.Object>();
}

public class ScriptableObjectIdAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(ScriptableObjectIdAttribute))]
public class ScriptableObjectIdDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        GUI.enabled = false;
        if (string.IsNullOrEmpty(property.stringValue))
        {
            property.stringValue = Guid.NewGuid().ToString();
        }
        EditorGUI.PropertyField(position, property, label, true);
        GUI.enabled = false;
    }
}
#endif