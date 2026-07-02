namespace Transiever.SieveRuler.Models;

/// <summary>
/// Supported rule condition categories.
/// </summary>
public enum RuleConditionType
{
    SenderContains,
    ReceiverContains,
    SubjectContains,
    BodyContains,
    SubjectOrBodyContains,
    HasAttachment
}
