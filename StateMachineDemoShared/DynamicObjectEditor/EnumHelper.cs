namespace StateMachineDemoShared.DynamicObjectEditor;

public static class EnumHelper
{
    //提取Enumtype中定义的所有枚举类型
    public static IEnumerable<KeyValuePair<Enum, string>> EnumStrList(Type enumType)
    {
        if (typeof(Enum).IsAssignableFrom(enumType))
        {
            foreach (var f in enumType.GetFields())
            {
                if (typeof(Enum).IsAssignableFrom(f.FieldType))
                    yield return new KeyValuePair<Enum, string>((Enum)f.GetValue(null)!, f.Name.ToString());
            }
        }
    }
}
