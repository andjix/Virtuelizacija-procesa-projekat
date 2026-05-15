using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class ValidationFault
    {
        private string message;
        private string fieldName;
        private string expectedRange;

        public ValidationFault() : this(string.Empty, string.Empty, string.Empty) { }

        public ValidationFault(string message, string fieldName, string expectedRange)
        {
            this.message = message;
            this.fieldName = fieldName;
            this.expectedRange = expectedRange;
        }

        [DataMember]
        public string Message { get => message; set => message = value; }

        [DataMember]
        public string FieldName { get => fieldName; set => fieldName = value; }

        [DataMember]
        public string ExpectedRange { get => expectedRange; set => expectedRange = value; }
    }
}