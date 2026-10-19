using System;
using UnityEngine;

namespace ImprovedHierarchy
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ButtonAttribute : PropertyAttribute
    {
        public string Name;
        public ButtonAttribute(string n = null) => Name = n;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class ReadOnlyAttribute : PropertyAttribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public class ShowIfAttribute : PropertyAttribute
    {
        public string Condition;
        public ShowIfAttribute(string c) => Condition = c;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class HideIfAttribute : PropertyAttribute
    {
        public string Condition;
        public HideIfAttribute(string c) => Condition = c;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class TabAttribute : PropertyAttribute
    {
        public string Name;
        public TabAttribute(string n) => Name = n;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class FoldoutAttribute : PropertyAttribute
    {
        public string Name;
        public FoldoutAttribute(string n) => Name = n;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class RequiredAttribute : PropertyAttribute
    {
        public string Message;
        public RequiredAttribute(string m = null) => Message = m;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class MinMaxSliderAttribute : PropertyAttribute
    {
        public float Min;
        public float Max;
        public MinMaxSliderAttribute(float min, float max) { Min = min; Max = max; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class ProgressBarAttribute : PropertyAttribute
    {
        public string Label;
        public float Max;
        public ProgressBarAttribute(float max, string label = null) { Max = max; Label = label; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class InfoBoxAttribute : PropertyAttribute
    {
        public string Message;
        public int MessageType; // 0=None, 1=Info, 2=Warning, 3=Error
        public InfoBoxAttribute(string message, int type = 1) { Message = message; MessageType = type; }
    }
}
