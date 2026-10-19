using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace ImprovedHierarchy
{
    public static class AttributeConditionResolver
    {
        public static bool Resolve(SerializedProperty property, string condition)
        {
            if (string.IsNullOrEmpty(condition) || property.serializedObject.targetObject == null) return true;

            Object target = property.serializedObject.targetObject;
            System.Type type = target.GetType();

            FieldInfo field = type.GetField(condition, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool)) return (bool)field.GetValue(target);

            PropertyInfo prop = type.GetProperty(condition, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.PropertyType == typeof(bool)) return (bool)prop.GetValue(target);

            MethodInfo method = type.GetMethod(condition, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null && method.ReturnType == typeof(bool) && method.GetParameters().Length == 0)
                return (bool)method.Invoke(target, null);

            return true;
        }
    }

    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public class ReadOnlyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            bool prev = GUI.enabled;
            GUI.enabled = false;
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = prev;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUI.GetPropertyHeight(property, label, true);
    }

    [CustomPropertyDrawer(typeof(ShowIfAttribute))]
    public class ShowIfDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (ShowIfAttribute)attribute;
            if (AttributeConditionResolver.Resolve(property, attr.Condition))
                EditorGUI.PropertyField(position, property, label, true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var attr = (ShowIfAttribute)attribute;
            return AttributeConditionResolver.Resolve(property, attr.Condition)
                ? EditorGUI.GetPropertyHeight(property, label, true)
                : -EditorGUIUtility.standardVerticalSpacing;
        }
    }

    [CustomPropertyDrawer(typeof(HideIfAttribute))]
    public class HideIfDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (HideIfAttribute)attribute;
            if (!AttributeConditionResolver.Resolve(property, attr.Condition))
                EditorGUI.PropertyField(position, property, label, true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var attr = (HideIfAttribute)attribute;
            return !AttributeConditionResolver.Resolve(property, attr.Condition)
                ? EditorGUI.GetPropertyHeight(property, label, true)
                : -EditorGUIUtility.standardVerticalSpacing;
        }
    }

    [CustomPropertyDrawer(typeof(RequiredAttribute))]
    public class RequiredDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (RequiredAttribute)attribute;
            bool isMissing = property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null;

            if (isMissing)
            {
                float helpHeight = EditorGUIUtility.singleLineHeight * 1.6f;
                Rect helpRect = new Rect(position.x, position.y, position.width, helpHeight);
                string msg = string.IsNullOrEmpty(attr.Message) ? $"'{property.displayName}' is required." : attr.Message;
                EditorGUI.HelpBox(helpRect, msg, MessageType.Warning);

                Rect fieldRect = new Rect(position.x, position.y + helpHeight + 2, position.width, EditorGUI.GetPropertyHeight(property, label, true));
                Color prev = GUI.color;
                GUI.color = new Color(1f, 0.7f, 0.7f);
                EditorGUI.PropertyField(fieldRect, property, label, true);
                GUI.color = prev;
            }
            else
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            bool isMissing = property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null;
            float baseHeight = EditorGUI.GetPropertyHeight(property, label, true);
            return isMissing ? baseHeight + EditorGUIUtility.singleLineHeight * 1.6f + 2 : baseHeight;
        }
    }

    [CustomPropertyDrawer(typeof(MinMaxSliderAttribute))]
    public class MinMaxSliderDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (MinMaxSliderAttribute)attribute;
            if (property.propertyType != SerializedPropertyType.Vector2)
            {
                EditorGUI.LabelField(position, label.text, "[MinMaxSlider] requires Vector2");
                return;
            }

            Vector2 value = property.vector2Value;
            Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            Rect sliderRect = new Rect(position.x + EditorGUIUtility.labelWidth + 30, position.y, position.width - EditorGUIUtility.labelWidth - 60, position.height);
            Rect minRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, 28, position.height);
            Rect maxRect = new Rect(position.x + position.width - 28, position.y, 28, position.height);

            EditorGUI.LabelField(labelRect, label);
            value.x = EditorGUI.FloatField(minRect, value.x);
            EditorGUI.MinMaxSlider(sliderRect, ref value.x, ref value.y, attr.Min, attr.Max);
            value.y = EditorGUI.FloatField(maxRect, value.y);

            value.x = Mathf.Clamp(value.x, attr.Min, value.y);
            value.y = Mathf.Clamp(value.y, value.x, attr.Max);
            property.vector2Value = value;
        }
    }

    [CustomPropertyDrawer(typeof(ProgressBarAttribute))]
    public class ProgressBarDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (ProgressBarAttribute)attribute;
            float value = property.propertyType == SerializedPropertyType.Float ? property.floatValue
                        : property.propertyType == SerializedPropertyType.Integer ? property.intValue
                        : 0f;
            float t = attr.Max <= 0 ? 0 : Mathf.Clamp01(value / attr.Max);
            string text = $"{(string.IsNullOrEmpty(attr.Label) ? property.displayName : attr.Label)}: {value:0.##}/{attr.Max:0.##}";
            EditorGUI.ProgressBar(position, t, text);
        }
    }

    [CustomPropertyDrawer(typeof(InfoBoxAttribute))]
    public class InfoBoxDrawer : DecoratorDrawer
    {
        public override void OnGUI(Rect position)
        {
            var attr = (InfoBoxAttribute)attribute;
            MessageType mt = attr.MessageType switch { 1 => MessageType.Info, 2 => MessageType.Warning, 3 => MessageType.Error, _ => MessageType.None };
            position.height -= 2f;
            EditorGUI.HelpBox(position, attr.Message, mt);
        }

        // Fit the message: long messages wrap instead of being clipped.
        public override float GetHeight()
        {
            var attr = (InfoBoxAttribute)attribute;
            bool hasIcon = attr.MessageType >= 1 && attr.MessageType <= 3;
            float width = EditorGUIUtility.currentViewWidth - 40f - (hasIcon ? 32f : 0f);
            float text = EditorStyles.helpBox.CalcHeight(new GUIContent(attr.Message), Mathf.Max(60f, width));
            return Mathf.Max(EditorGUIUtility.singleLineHeight * (hasIcon ? 2.2f : 1.4f), text + 6f) + 2f;
        }
    }
}
