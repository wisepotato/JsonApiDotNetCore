using JsonApiDotNetCore.Models;

namespace GettingStarted
{
    public class Article : Identifiable
    {
        [Attr("title")]
        public string Title { get; set; }
    }
}