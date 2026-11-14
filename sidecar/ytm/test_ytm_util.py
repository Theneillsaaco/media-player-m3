"""Run from sidecar/ytm:  python3 -m unittest test_ytm_util"""
import unittest

from ytm_util import clock


class ClockTests(unittest.TestCase):
    def test_minutes_and_seconds(self):
        self.assertEqual(clock("3:21"), 201)

    def test_hours(self):
        self.assertEqual(clock("1:02:03"), 3723)

    def test_not_a_time_is_zero(self):
        for text in ("", None, "abc", "3", "1:2:3:4", "a:b", "-1:00", "3:2x"):
            self.assertEqual(clock(text), 0, text)

    def test_numbers_are_read_as_text(self):
        self.assertEqual(clock(5), 0)


if __name__ == "__main__":
    unittest.main()
