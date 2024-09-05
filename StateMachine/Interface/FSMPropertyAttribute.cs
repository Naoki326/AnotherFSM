using System;

namespace StateMachine.Interface
{
    /// <summary>
    /// 节点属性，用于快速生成流程配置界面
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class FSMPropertyAttribute : Attribute
    {
        /// <summary>
        /// 显示的名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 是否必须赋值
        /// </summary>
        public bool IsRequired { get; set; }

        /// <summary>
        /// 是否必须赋值
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// 当前显示的优先级
        /// </summary>
        public int Priority { get; set; }

        public FSMPropertyAttribute(string name, bool isRequired, int priority)
        {
            Name = name;
            IsRequired = isRequired;
            Priority = priority;
        }

        public FSMPropertyAttribute(string name, bool isVisible, bool isRequired, int priority)
        {
            Name = name;
            IsVisible = isVisible;
            IsRequired = isRequired;
            Priority = priority;
        }
    }
}
