using StateMachine.Interface;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StateMachine
{

    /// <summary>
    /// 用来指定FSMNode类型的序列化方式
    /// </summary>
    public class FSMJsonConverter : JsonConverter<IFSMNode>
    {

        public override bool CanConvert(Type typeToConvert)
        {
            // 只处理有 InverseSerialize 特性的类型
            return typeof(IFSMNode).IsAssignableFrom(typeToConvert);
        }

        public override void Write(Utf8JsonWriter writer, IFSMNode value, JsonSerializerOptions options)
        {
            var type = value.GetType();
            writer.WriteStartObject();

            writer.WritePropertyName("$id");
            writer.WriteStringValue("1");

            writer.WritePropertyName("IFSMNode");
            writer.WriteStringValue(value.GetType().Name);

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase))
            {
                var requireSerializeAttribute = property.GetCustomAttribute<FSMPropertyAttribute>();
                if (requireSerializeAttribute != null)
                {
                    var propertyValue = property.GetValue(value);
                    writer.WritePropertyName(property.Name.ToLower());
                    JsonSerializer.Serialize(writer, propertyValue, options);
                }
            }

            writer.WriteEndObject();
        }

        private Lazy<IEnumerable<Type>> DeriveTypes = new Lazy<IEnumerable<Type>>(IoC.GetAllTypes<IFSMNode>);

        public override IFSMNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException();
            }

            reader.Read();
            string id = reader.GetString();
            reader.Read();
            string idStr = reader.GetString();

            reader.Read();
            string fsmNodeType = reader.GetString();
            if (fsmNodeType != "IFSMNode")
            {
                throw new JsonException();
            }
            reader.Read();
            string typeName = reader.GetString();

            var typeType = DeriveTypes.Value.First(p => p.Name.Contains(typeName));
            var instance = Activator.CreateInstance(typeType);

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return instance as IFSMNode;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString();
                    reader.Read();

                    var property = typeType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (property != null && property.GetCustomAttribute<FSMPropertyAttribute>() != null)
                    {
                        var propertyValue = JsonSerializer.Deserialize(ref reader, property.PropertyType, options);
                        property.SetValue(instance, propertyValue);
                    }
                    else
                    {
                        // Skip the value if the property does not have RequireSerialize attribute
                        reader.Skip();
                    }
                }
            }

            throw new JsonException();
        }
    }
}
