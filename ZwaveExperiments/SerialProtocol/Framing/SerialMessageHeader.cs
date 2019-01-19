using System.Diagnostics.CodeAnalysis;

namespace ZwaveExperiments.SerialProtocol.Framing
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    enum SerialFrameHeader : byte
    {
        /// <summary>
        /// Start of frame (SOF), signal a data frame.
        /// </summary>
        SOF = 0x01,
        
        /// <summary>
        /// Previous message acknowledgment. (ACK)
        /// </summary>
        ACK = 0x06,
        
        /// <summary>
        /// Previous message was an error. (NAK)
        /// </summary>
        NAK = 0x15,
        
        /// <summary>
        /// Retransmission request (CAN)
        /// </summary>
        CAN = 0x18
    }
}