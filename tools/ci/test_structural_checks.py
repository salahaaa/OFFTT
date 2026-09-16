import unittest
from structural_checks import code_only, double_members, empty_handlers


class StructuralGuardTests(unittest.TestCase):
    def test_doc_comment_is_not_an_empty_button(self):
        self.assertEqual([], empty_handlers('/// Old code: WithPrint((_, _) => { })\n'))

    def test_block_comment_and_literal_are_not_buttons(self):
        self.assertEqual([], empty_handlers('/* WithSave((_, _) => {}) */\nvar example = "WithPrint((_, _) => {})";'))

    def test_real_handler_is_detected_including_multiline_and_comment_body(self):
        self.assertEqual([2], empty_handlers('var x = toolbar\n.WithSave(\n (_, _) => { /* TODO */\n });'))

    def test_working_handler_is_not_rejected(self):
        self.assertEqual([], empty_handlers('toolbar.WithPrint((_, _) => Print());'))

    def test_literals_with_comment_markers_preserve_following_code(self):
        self.assertIn('var b', code_only('var s = "https://example.test"; var b = 1;'))

    def test_double_members_identify_class_and_computed_values(self):
        text = 'class Lot { public double Qty { get; set; } public double? Duration {get;set;} public double Free => Qty; }'
        self.assertEqual({'R.cs:Lot.Qty', 'R.cs:Lot.Duration', 'R.cs:Lot.Free'}, double_members('R.cs', text))

    def test_equal_count_cannot_hide_a_new_member(self):
        old = double_members('R.cs', 'class Lot { public double Qty {get;set;} }')
        new = double_members('R.cs', 'class Lot { public double Price {get;set;} }')
        self.assertEqual({'R.cs:Lot.Price'}, new - old)


if __name__ == '__main__':
    unittest.main()
