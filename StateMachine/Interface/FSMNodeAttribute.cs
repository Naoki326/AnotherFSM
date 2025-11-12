
namespace StateMachine
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class FSMNodeAttribute : Attribute
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

        public string Key { get; set; }

        public FSMNodeAttribute(string key)
        {
            Key = key;
        }

        public FSMNodeAttribute(string key, int[] indexes, string[] events) : this(key)
        {
            Indexes = indexes;
            EventDescriptions = events;
        }

        public FSMNodeAttribute(string key, string nodeDiscription, int[] indexes, string[] events) : this(key, indexes, events)
        {
            NodeDescription = nodeDiscription;
        }

    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class FSMNodeSourceAttribute : Attribute
    {
        public string SourceCSPath { get; }
        public int[] Events { get; }

        public FSMNodeSourceAttribute(string sourceCSPath, int[] events)
        {
            SourceCSPath = sourceCSPath;
            Events = events ?? System.Array.Empty<int>();
        }
    }
}
