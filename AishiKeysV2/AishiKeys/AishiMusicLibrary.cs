using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AishiKeys
{
    internal static class AishiMusicLibrary
    {
        internal static string[] Folders(string root)
        {
            return new[]
            {
                root
            }.Concat(Directory.EnumerateDirectories(root).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Take(200)).ToArray();
        }

        internal static string Create(string root, string name)
        {
            name = (name ?? "").Trim();
            string stem = name.Split('.')[0];
            string[] reserved =
            {
                "CON",
                "PRN",
                "AUX",
                "NUL",
                "COM1",
                "COM2",
                "COM3",
                "COM4",
                "COM5",
                "COM6",
                "COM7",
                "COM8",
                "COM9",
                "LPT1",
                "LPT2",
                "LPT3",
                "LPT4",
                "LPT5",
                "LPT6",
                "LPT7",
                "LPT8",
                "LPT9"
            };
            if (name.Length == 0 || name.Length > 64 || name == "." || name == ".." || name.EndsWith(".") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || reserved.Contains(stem, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Use a folder name of 1–64 characters without path separators or reserved names.");
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(fullRoot, name));
            if (!string.Equals(Path.GetDirectoryName(path), fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid playlist folder.");
            if (Directory.Exists(path) || File.Exists(path))
                throw new IOException("A file or playlist with this name already exists.");
            if (Directory.EnumerateDirectories(fullRoot).Take(200).Count() >= 200)
                throw new IOException("Limit of 200 playlist folders reached.");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    internal sealed class AishiMusicOrder
    {
        private readonly Random _random;
        private readonly List<int> _bag = new List<int>(), _history = new List<int>();
        private int _count, _position = -1;
        private bool _initialBag = true;
        internal AishiMusicOrder() : this(new Random())
        {
        }

        internal AishiMusicOrder(Random random)
        {
            _random = random;
        }

        private int Current => _position >= 0 ? _history[_position] : -1;

        internal void Reset(int count, int current = -1)
        {
            _count = count;
            _bag.Clear();
            _history.Clear();
            _position = -1;
            _initialBag = true;
            if (current >= 0 && current < count)
                Select(current);
        }

        internal void Select(int index)
        {
            _bag.Clear();
            _initialBag = true;
            Record(index);
        }

        private int Record(int index)
        {
            if (_position + 1 < _history.Count)
                _history.RemoveRange(_position + 1, _history.Count - _position - 1);
            _history.Add(index);
            if (_history.Count > 500)
                _history.RemoveAt(0);
            _position = _history.Count - 1;
            _bag.Remove(index);
            return index;
        }

        internal int Next(bool shuffle)
        {
            if (_count == 0)
                return -1;
            if (_position + 1 < _history.Count)
                return _history[++_position];
            if (!shuffle)
                return Record((Current + 1) % _count);
            if (_bag.Count == 0)
            {
                for (int i = 0; i < _count; i++)
                    if (!_initialBag || _count == 1 || i != Current)
                        _bag.Add(i);
                _initialBag = false;
                for (int i = _bag.Count - 1; i > 0; i--)
                {
                    int j = _random.Next(i + 1);
                    int value = _bag[i];
                    _bag[i] = _bag[j];
                    _bag[j] = value;
                }

                if (_bag.Count > 1 && _bag[_bag.Count - 1] == Current)
                {
                    int value = _bag[0];
                    _bag[0] = Current;
                    _bag[_bag.Count - 1] = value;
                }
            }

            return Record(_bag[_bag.Count - 1]);
        }

        internal int Previous(bool shuffle)
        {
            if (_count == 0)
                return -1;
            if (_position > 0)
                return _history[--_position];
            if (shuffle)
                return Current < 0 ? Next(true) : Current;
            return Record((Current - 1 + _count) % _count);
        }
    }
}
