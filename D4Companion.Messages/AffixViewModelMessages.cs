using CommunityToolkit.Mvvm.Messaging.Messages;

namespace D4Companion.Messages
{
    public class AffixLanguageChangedMessage
    {
    }

    public class AffixPresetChangedMessage(AffixPresetChangedMessageParams affixPresetChangedMessageParams) : ValueChangedMessage<AffixPresetChangedMessageParams>(affixPresetChangedMessageParams)
    {
    }

    public class AffixPresetChangedMessageParams
    {
        public string PresetName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Published by AffixViewModel whenever the selected affix preset (build) changes.
    /// </summary>
    public class SelectedAffixPresetUpdatedMessage(AffixPresetChangedMessageParams affixPresetChangedMessageParams) : ValueChangedMessage<AffixPresetChangedMessageParams>(affixPresetChangedMessageParams)
    {
    }

    /// <summary>
    /// Asks AffixViewModel to select the preset with the given name. AffixViewModel stays the owner of the selection.
    /// </summary>
    public class SelectAffixPresetRequestMessage(AffixPresetChangedMessageParams affixPresetChangedMessageParams) : ValueChangedMessage<AffixPresetChangedMessageParams>(affixPresetChangedMessageParams)
    {
    }

    public class ToggleOverlayFromGUIMessage(ToggleOverlayFromGUIMessageParams toggleOverlayFromGUIMessageParams) : ValueChangedMessage<ToggleOverlayFromGUIMessageParams>(toggleOverlayFromGUIMessageParams)
    {
    }

    public class ToggleOverlayFromGUIMessageParams
    {
        public bool IsEnabled { get; set; } = false;
    }

    /// <summary>
    /// Message is published by the following events:
    /// - Changing the controller images
    /// </summary>
    public class AvailableImagesChangedMessage
    {
    }
}
