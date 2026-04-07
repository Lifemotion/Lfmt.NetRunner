using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lfmt.NetRunner.Filters;

public class ApiExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ApiExceptionFilter> _logger;

    public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        _logger.LogError(context.Exception, "API error: {Path}", context.HttpContext.Request.Path);

        context.Result = new JsonResult(new { error = context.Exception.Message })
        {
            StatusCode = 500
        };
        context.ExceptionHandled = true;
    }
}
