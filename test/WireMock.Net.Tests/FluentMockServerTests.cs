﻿using NFluent;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace WireMock.Net.Tests
{
    public class FluentMockServerTests
    {
        [Fact]
        public async Task FluentMockServer_Should_respond_to_request_bodyAsBase64()
        {
            // given
            var server = FluentMockServer.Start();

            server.Given(Request.Create().WithPath("/foo").UsingGet()).RespondWith(Response.Create().WithBodyFromBase64("SGVsbG8gV29ybGQ/"));

            // when
            var response = await new HttpClient().GetStringAsync("http://localhost:" + server.Ports[0] + "/foo");

            // then
            Check.That(response).IsEqualTo("Hello World?");
        }

        [Fact]
        public async Task FluentMockServer_Should_reset_requestlogs()
        {
            // given
            var server = FluentMockServer.Start();

            // when
            await new HttpClient().GetAsync("http://localhost:" + server.Ports[0] + "/foo");
            server.ResetLogEntries();

            // then
            Check.That(server.LogEntries).IsEmpty();
        }

        [Fact]
        public void FluentMockServer_Should_reset_mappings()
        {
            // given
            string path = $"/foo_{Guid.NewGuid()}";
            var server = FluentMockServer.Start();

            server
                .Given(Request.Create()
                    .WithPath(path)
                    .UsingGet())
                .RespondWith(Response.Create()
                    .WithBody(@"{ msg: ""Hello world!""}"));

            // when
            server.ResetMappings();

            // then
            Check.That(server.Mappings).IsEmpty();
            Check.ThatAsyncCode(() => new HttpClient().GetStringAsync("http://localhost:" + server.Ports[0] + path)).ThrowsAny();
        }

        [Fact]
        public async Task FluentMockServer_Should_respond_a_redirect_without_body()
        {
            // Assign
            string path = $"/foo_{Guid.NewGuid()}";
            string pathToRedirect = $"/bar_{Guid.NewGuid()}";

            var server = FluentMockServer.Start();

            server
                .Given(Request.Create()
                    .WithPath(path)
                    .UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(307)
                    .WithHeader("Location", pathToRedirect));
            server
                .Given(Request.Create()
                    .WithPath(pathToRedirect)
                    .UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithBody("REDIRECT SUCCESSFUL"));

            // Act
            var response = await new HttpClient().GetStringAsync($"http://localhost:{server.Ports[0]}{path}");

            // Assert
            Check.That(response).IsEqualTo("REDIRECT SUCCESSFUL");
        }

        [Fact]
        public async Task FluentMockServer_Should_delay_responses_for_a_given_route()
        {
            // given
            var server = FluentMockServer.Start();

            server
                .Given(Request.Create()
                    .WithPath("/*"))
                .RespondWith(Response.Create()
                    .WithBody(@"{ msg: ""Hello world!""}")
                    .WithDelay(TimeSpan.FromMilliseconds(200)));

            // when
            var watch = new Stopwatch();
            watch.Start();
            await new HttpClient().GetStringAsync("http://localhost:" + server.Ports[0] + "/foo");
            watch.Stop();

            // then
            Check.That(watch.ElapsedMilliseconds).IsStrictlyGreaterThan(200);
        }

        [Fact]
        public async Task FluentMockServer_Should_delay_responses()
        {
            // given
            var server = FluentMockServer.Start();
            server.AddGlobalProcessingDelay(TimeSpan.FromMilliseconds(200));
            server
                .Given(Request.Create().WithPath("/*"))
                .RespondWith(Response.Create().WithBody(@"{ msg: ""Hello world!""}"));

            // when
            var watch = new Stopwatch();
            watch.Start();
            await new HttpClient().GetStringAsync("http://localhost:" + server.Ports[0] + "/foo");
            watch.Stop();

            // then
            Check.That(watch.ElapsedMilliseconds).IsStrictlyGreaterThan(200);
        }

        //Leaving commented as this requires an actual certificate with password, along with a service that expects a client certificate
        //[Fact]
        //public async Task Should_proxy_responses_with_client_certificate()
        //{
        //    // given
        //    var _server = FluentMockServer.Start();
        //    _server
        //        .Given(Request.Create().WithPath("/*"))
        //        .RespondWith(Response.Create().WithProxy("https://server-that-expects-a-client-certificate", @"\\yourclientcertificatecontainingprivatekey.pfx", "yourclientcertificatepassword"));

        //    // when
        //    var result = await new HttpClient().GetStringAsync("http://localhost:" + _server.Ports[0] + "/someurl?someQuery=someValue");

        //    // then
        //    Check.That(result).Contains("google");
        //}

        [Fact]
        public async Task FluentMockServer_Should_exclude_restrictedResponseHeader()
        {
            // Assign
            string path = $"/foo_{Guid.NewGuid()}";
            var server = FluentMockServer.Start();

            server
                .Given(Request.Create().WithPath(path).UsingGet())
                .RespondWith(Response.Create().WithHeader("Transfer-Encoding", "chunked").WithHeader("test", "t"));

            // Act
            var response = await new HttpClient().GetAsync("http://localhost:" + server.Ports[0] + path);

            // Assert
            Check.That(response.Headers.Contains("test")).IsTrue();
            Check.That(response.Headers.Contains("Transfer-Encoding")).IsFalse();
        }

        [Theory]
        [InlineData("DELETE")]
#if !NET452
        [InlineData("TRACE")]
        [InlineData("GET")]
#endif
        public async Task FluentMockServer_Should_exclude_body_for_methods_where_body_is_definitely_disallowed(string method)
        {
            // Assign
            string content = "hello";
            var server = FluentMockServer.Start();

            server
                .Given(Request.Create().WithBody(b => true))
                .AtPriority(0)
                .RespondWith(Response.Create().WithStatusCode(400));
            server
                .Given(Request.Create())
                .AtPriority(1)
                .RespondWith(Response.Create().WithStatusCode(200));

            // Act
            var request = new HttpRequestMessage(new HttpMethod(method), "http://localhost:" + server.Ports[0] + "/");
            request.Content = new StringContent(content);
            var response = await new HttpClient().SendAsync(request);

            // Assert
            Check.That(response.StatusCode).Equals(HttpStatusCode.OK);
        }
    
        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("OPTIONS")]
        [InlineData("REPORT")]
        [InlineData("SOME-UNKNOWN-METHOD")] // default behavior for unknown methods is to allow a body (see BodyParser.ShouldParseBody)
        public async Task FluentMockServer_Should_not_exclude_body_for_supported_methods(string method)
        {
            // Assign
            string content = "hello";
            var server = FluentMockServer.Start();

            server
                .Given(Request.Create().WithBody(content))
                .AtPriority(0)
                .RespondWith(Response.Create().WithStatusCode(200));
            server
                .Given(Request.Create())
                .AtPriority(1)
                .RespondWith(Response.Create().WithStatusCode(400));

            // Act
            var request = new HttpRequestMessage(new HttpMethod(method), "http://localhost:" + server.Ports[0] + "/");
            request.Content = new StringContent(content);
            var response = await new HttpClient().SendAsync(request);

            // Assert
            Check.That(response.StatusCode).Equals(HttpStatusCode.OK);
        }
    }
}