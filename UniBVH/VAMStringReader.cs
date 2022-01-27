using System;
using System.Text;

public class VAMStringReader
    {
       
    string _s;
    private int _pos;

    private int _length;

    public VAMStringReader(string[] lines) : this(ConvertArrayToString(lines)) { }

    public static string ConvertArrayToString(string[] lines)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string st in lines)
        {
            sb.Append(st);
        }
        return sb.ToString();
    }

    public VAMStringReader(string s)
    {
        if (s == null)
        {
            throw new ArgumentNullException("s");
        }
        _s = s;
        _length = (s?.Length ?? 0);
    }

    public int Peek()
    {
        if (_s == null)
        {
            throw new NullReferenceException("s");
        }
        if (_pos == _length)
        {
            return -1;
        }
        return _s[_pos];
    }

    public int Read()
    {
        if (_s == null)
        {
            throw new NullReferenceException("s");
        }
        if (_pos == _length)
        {
            return -1;
        }
        return _s[_pos++];
    }


    public string ReadLine()
    {
        if (_s == null)
        {
            return null;
        }
        int i;
        for (i = _pos; i < _length; i++)
        {
            char c = _s[i];
            if (c == '\r' || c == '\n')
            {
                string result = _s.Substring(_pos, i - _pos);
                _pos = i + 1;
                if (c == '\r' && _pos < _length && _s[_pos] == '\n')
                {
                    _pos++;
                }
                return result;
            }
        }
        if (i > _pos)
        {
            string result2 = _s.Substring(_pos, i - _pos);
            _pos = i;
            return result2;
        }
        return null;
    }

}
