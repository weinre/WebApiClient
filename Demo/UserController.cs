﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetworkSocket;
using NetworkSocket.Http;

namespace Demo
{
    /// <summary>
    /// User控制器
    /// </summary>
    [Route("/webapi/user/{action}")]
    public class UserController : HttpController
    {
        [HttpGet]
        public ActionResult About(UserInfo user, string something)
        {
            var about = new StringBuilder()
                .AppendLine()
                .Append("UserInfo:").AppendLine(user.ToString())
                .Append("Something:").Append(something);

            var keys = Request.Headers.Keys;
            foreach (var key in keys.Cast<string>().Reverse())
            {
                var value = Request.Headers.TryGet<string>(key, null);
                about.Insert(0, key + ": " + value + "\r\n");
            }

            return Content(about.ToString());
        }

        [HttpGet]
        public JsonResult GetById(string id)
        {
            var model = new UserInfo();
            return Json(model);
        }

        [HttpGet]
        public JsonResult GetByAccount(string account)
        {
            var model = new UserInfo { Account = account };
            return Json(model);
        }

        [HttpPost]
        public JsonResult UpdateWithForm(UserInfo user, string name, string nickName, int? age)
        {
            user.Account = "xyz";
            return Json(user);
        }

        [HttpPost]
        public JsonResult UpdateWithJson([Body] UserInfo user)
        {
            return Json(user);
        }

        [HttpPost]
        public ActionResult UpdateWithXml()
        {
            var xml = Encoding.UTF8.GetString(Request.Body);
            return Content(xml);
        }

        [HttpPost]
        public ActionResult UpdateWithMulitpart(UserInfo user , string nickName, int? age)
        {
            return Json(user);
        }

        protected override void OnExecuting(ActionContext filterContext)
        {
            Console.WriteLine("{0} HttpServer收到http请求：{1}", DateTime.Now.ToString("HH:mm:ss.fff"), filterContext.Action.Route);
        }
    }
}
