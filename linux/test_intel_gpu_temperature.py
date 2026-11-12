import unittest
from intel_gpu_temperature import decode


class TemperatureTests(unittest.TestCase):
    def test_live_arrowlake_registers(self):
        self.assertEqual(34, decode(0x80691480, 0x88470000))

    def test_invalid_status(self):
        self.assertIsNone(decode(0x80691480, 0x08470000))
        self.assertIsNone(decode(0xffffffff, 0xffffffff))

    def test_bad_target_or_delta(self):
        self.assertIsNone(decode(0, 0x88470000))
        self.assertIsNone(decode(0x80691480, 0x88ff0000))

    def test_temperature_change(self):
        self.assertEqual(40, decode(0x80691480, 0x88410000))


if __name__ == '__main__':
    unittest.main()
