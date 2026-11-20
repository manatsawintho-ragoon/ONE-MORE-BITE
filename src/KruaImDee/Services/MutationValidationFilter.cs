using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KruaImDee.Services;

public sealed class MutationValidationFilter : IActionFilter, IResultFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (
            !HttpMethods.IsGet(context.HttpContext.Request.Method)
            && !HttpMethods.IsHead(context.HttpContext.Request.Method)
            && !context.ModelState.IsValid
        )
        {
            if (context.HttpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var errors = new Dictionary<string, string>();
                foreach (var field in context.ModelState)
                {
                    if (field.Value != null && field.Value.Errors.Count > 0)
                    {
                        errors[field.Key] = "กรุณากรอกข้อมูลให้ตรงตามรูปแบบที่กำหนด";
                    }
                }

                context.Result = new JsonResult(
                    new { message = "กรุณาตรวจสอบรูปแบบข้อมูลในช่องที่ระบุ", errors }
                )
                {
                    StatusCode = 400,
                };
                return;
            }

            context.Result = new ViewResult
            {
                ViewName = "~/Views/Shared/InvalidInput.cshtml",
                StatusCode = 400,
            };
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.HttpContext.Request.Headers["X-Requested-With"] != "XMLHttpRequest")
        {
            return;
        }

        var controller = context.Controller as Controller;
        if (controller == null)
        {
            return;
        }

        string? redirectUrl = null;
        if (context.Result is RedirectResult redirect)
        {
            redirectUrl = redirect.Url;
        }
        else if (context.Result is LocalRedirectResult local)
        {
            redirectUrl = local.Url;
        }
        else if (context.Result is RedirectToActionResult action)
        {
            redirectUrl = controller.Url.Action(
                action.ActionName,
                action.ControllerName,
                action.RouteValues
            );
        }

        // Let the browser load the destination once and keep its success message.
        if (redirectUrl != null)
        {
            context.Result = new JsonResult(new { redirectUrl });
        }
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
