namespace DeltaStudio.Core.Common;

/// <summary>Honest implementation status marker used across the whole codebase and docs.</summary>
public enum CapabilityStatus
{
    /// <summary>Fully implemented and covered by automated tests.</summary>
    Implemented,
    /// <summary>Implemented, but a documented portion of the behaviour is missing or unverified.</summary>
    Partial,
    /// <summary>Works against mocks/simulators only; never validated against real hardware.</summary>
    Experimental,
    /// <summary>Intentionally not implemented; the API exists and reports this status instead of guessing.</summary>
    NotImplemented,
}

/// <summary>Status of a wire protocol implementation, per the DeltaStudio safety policy.</summary>
public enum ProtocolStatus
{
    /// <summary>Implemented against published, verified documentation and exercised by tests.</summary>
    Supported,
    /// <summary>Some operations implemented; others missing. Callers must check per operation.</summary>
    PartiallySupported,
    /// <summary>Abstraction exists, implementation intentionally absent (undocumented/proprietary).</summary>
    NotImplemented,
}
