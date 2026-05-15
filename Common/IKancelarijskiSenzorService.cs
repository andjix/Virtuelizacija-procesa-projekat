using System.ServiceModel;

namespace Common
{
    [ServiceContract]
    public interface IKancelarijskiSenzorService
    {
        [OperationContract]
        [FaultContract(typeof(DataFormatFault))]
        [FaultContract(typeof(ValidationFault))]
        OperationResponse StartSession(SessionMeta meta);

        [OperationContract]
        [FaultContract(typeof(DataFormatFault))]
        [FaultContract(typeof(ValidationFault))]
        OperationResponse PushSample(SensorSample sample);

        [OperationContract]
        OperationResponse EndSession();
    }
}
