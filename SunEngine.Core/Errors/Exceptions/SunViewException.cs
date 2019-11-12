using System.Linq;

namespace SunEngine.Core.Errors.Exceptions
{
    public class SunViewException : SunException
    {
        public ErrorList ErrorList { get; }

        public SunViewException(ErrorList errorList)
            : base(string.Join(",", errorList.Errors.Select(x =>
                $"{x.Description} {x.Message}")))
        {
            ErrorList = errorList;
        }
    }
}
