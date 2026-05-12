using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class DataFormatFault
    {
        private string message;
        private string fieldName;

        public DataFormatFault(string message, string fieldName)
        {
            this.message = message;
            this.fieldName = fieldName;
        }

        [DataMember]
        public string Message { get => message; set => message = value; }

        [DataMember]
        public string FieldName { get => fieldName; set => fieldName = value; }
    }
}
