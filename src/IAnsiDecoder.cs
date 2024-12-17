using System.Text;

namespace libVT100
{
    public interface IAnsiDecoder : IDecoder
    {
        void Subscribe ( IAnsiDecoderClient _client );
        void UnSubscribe ( IAnsiDecoderClient _client );
        StringBuilder? dvt { get; set; }
        void deb(string s);
        void deb(char c);
    }
}
