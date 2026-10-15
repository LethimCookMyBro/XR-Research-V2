import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CHANNEL = ROOT / "Assets/VRMaintenanceResearch/ResearchFlow/Runtime/ResearchAdbControlChannel.cs"
PROVIDER = ROOT / "Assets/Plugins/Android/QuestProfile.androidlib/src/th/ac/tmu/vr/researchflow/transport/QuestProfileProvider.java"


def channel_commands():
    text = CHANNEL.read_text(encoding="utf-8")
    body = text[text.index("object Execute(string command, JObject request)"):]
    body = body[:body.index("default: return Fail(")]
    return set(re.findall(r'case "([a-z_]+)":', body))


def provider_commands():
    text = PROVIDER.read_text(encoding="utf-8")
    listed = re.search(r"COMMANDS = Arrays\.asList\(([^)]*)\)", text).group(1)
    return set(re.findall(r'"([a-z_]+)"', listed))


class QuestProfileCommandParity(unittest.TestCase):
    def test_every_channel_command_passes_the_android_provider(self):
        self.assertEqual(channel_commands() - provider_commands(), set())

    def test_provider_allows_no_command_the_channel_cannot_handle(self):
        self.assertEqual(provider_commands() - channel_commands(), set())


if __name__ == "__main__":
    unittest.main()
