import hashlib
from pathlib import Path
import tempfile
import unittest

from capture_reference import capture


class CaptureReferenceTests(unittest.TestCase):
    def test_captures_context_actions_and_bindings_without_changing_source(self):
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary)
            content = b'''<Window xmlns="urn:ui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="Example.View">
              <Button x:Name="Connect" Command="{Binding ConnectCommand}" Content="Connect" />
              <MenuItem Header="Transfer settings" Click="Transfer_Click" />
              <TextBox TextChanged="Filter_Changed" />
            </Window>'''
            (source / "View.xaml").write_bytes(content)
            manifest = capture(source)
            view = manifest["uiCandidates"][0]
            self.assertEqual("Example.View", view["class"])
            self.assertEqual(["Connect"], view["namedElements"])
            self.assertEqual(3, len(view["actions"]))
            self.assertEqual("{Binding ConnectCommand}", view["actions"][0]["attributes"]["Command"])
            self.assertTrue(view["required"])
            self.assertEqual("pending", view["visualStatus"])
            self.assertEqual(hashlib.sha256(content).hexdigest(), manifest["sourceFiles"][0]["sha256"])
            self.assertEqual(content, (source / "View.xaml").read_bytes())

    def test_ignores_generated_trees_but_includes_code_only_and_web_features(self):
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary)
            for folder in ["obj", "node_modules", "Features"]:
                (source / folder).mkdir()
            (source / "obj" / "Generated.cs").write_text("generated")
            (source / "node_modules" / "library.js").write_text("vendor")
            (source / "Features" / "Clipboard.cs").write_text("feature")
            (source / "Features" / "Scanner.tsx").write_text("scanner")
            paths = [item["path"] for item in capture(source)["sourceFiles"]]
            self.assertEqual(["Features/Clipboard.cs", "Features/Scanner.tsx"], paths)


if __name__ == "__main__":
    unittest.main()
