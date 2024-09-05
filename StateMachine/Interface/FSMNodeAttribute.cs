using Autofac.Annotation;

namespace StateMachine
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class FSMNodeAttribute : Component
    {
        //该Node发出的事件的index
        public int[] Indexes;

        //推荐的事件名（也是该事件对当前流程的意义）
        //长度必须与Indexes一致
        public string[] EventDescriptions;

        //NodeDescription 描述信息，用于界面显示
        public string? NodeDescription { get; set; }

        //用于界面排序
        public int Id { get; set; } = new Random().Next(100, int.MaxValue - 1);

        public FSMNodeAttribute(string key, int[] indexes, string[] events) : base(key)
        {
            Indexes = indexes;
            EventDescriptions = events;
        }

        public FSMNodeAttribute(string key, string nodeDiscription, int[] indexes, string[] events) : this(key, indexes, events)
        {
            NodeDescription = nodeDiscription;
        }

    }
}
