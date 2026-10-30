#include "MatchManager.hpp"
#include <boost/asio.hpp>
#include <boost/beast.hpp>
#include <deque>
#include <set>
#include <csignal>
#include <iostream>
namespace ProjectC::Network
{
namespace
{
namespace net = boost::asio;
namespace beast = boost::beast;
namespace websocket = beast::websocket;
using Tcp = net::ip::tcp;
class Connection : public std::enable_shared_from_this<Connection>
{
    websocket::stream<beast::tcp_stream> socket;
    beast::flat_buffer input;
    std::deque<std::string> output;
    std::size_t queuedBytes = 0;
    MatchManager& match;
    std::shared_ptr<ClientSession> client = std::make_shared<ClientSession>();
    bool stopped = false;
    std::function<void(std::shared_ptr<Connection>)> onStop;
public:
    Connection(Tcp::socket peer, MatchManager& session, std::function<void(std::shared_ptr<Connection>)> cleanup)
        : socket(std::move(peer)), match(session), onStop(std::move(cleanup)) {}
    void Stop()
    {
        if (stopped) return;
        stopped = true;
        match.Disconnect(client);
        boost::system::error_code ignored;
        beast::get_lowest_layer(socket).socket().shutdown(Tcp::socket::shutdown_both, ignored);
        beast::get_lowest_layer(socket).socket().close(ignored);
        onStop(shared_from_this());
        // Outstanding write owns output.front() until its completion callback.
    }
    void Start()
    {
        auto weak = weak_from_this();
        client->Send = [weak](Json const& message) { if (auto self = weak.lock()) self->Send(message.dump()); };
        socket.read_message_max(16384);
        socket.set_option(websocket::stream_base::timeout::suggested(beast::role_type::server));
        socket.async_accept([self = shared_from_this()](boost::system::error_code error) {
            if (error) self->Stop(); else self->Read();
        });
    }
private:
    void Read()
    {
        socket.async_read(input, [self = shared_from_this()](boost::system::error_code error, std::size_t) {
            if (error) { self->Stop(); return; }
            if (!self->socket.got_text()) { self->Stop(); return; }
            auto message = beast::buffers_to_string(self->input.data());
            self->input.consume(self->input.size());
            self->match.Receive(self->client, message);
            if (!self->stopped) self->Read();
        });
    }
    void Send(std::string message)
    {
        if (stopped) return;
        if (output.size() >= 128 || queuedBytes + message.size() > 1024 * 1024) { Stop(); return; }
        queuedBytes += message.size();
        output.push_back(std::move(message));
        if (output.size() == 1) Write();
    }
    void Write()
    {
        socket.text(true);
        socket.async_write(net::buffer(output.front()), [self = shared_from_this()](boost::system::error_code error, std::size_t) {
            if (error) { self->Stop(); return; }
            self->queuedBytes -= self->output.front().size();
            self->output.pop_front();
            if (!self->stopped && !self->output.empty()) self->Write();
        });
    }
};
}
void RunServer(ServerConfig const& config)
{
    // One io_context::run thread: all callbacks and MatchSession mutations are serialized.
    // MatchSession has no socket/Beast dependency and can also be exercised directly.
    net::io_context io(1);
    MatchManager match(config);
    std::set<std::shared_ptr<Connection>> connections;
    Tcp::acceptor acceptor(io, {Tcp::v4(), config.Port});
    std::function<void()> accept;
    accept = [&] {
        acceptor.async_accept([&](boost::system::error_code error, Tcp::socket peer) {
            if (!error)
            {
                if (connections.size() >= config.MaxMatches * 2 + 16) { boost::system::error_code ignored; peer.close(ignored); }
                else
                {
                    auto connection = std::make_shared<Connection>(std::move(peer), match,
                        [&](auto self) { connections.erase(self); std::cout << "Clients=" << connections.size() << " activeMatches=" << match.ActiveCount() << std::endl; });
                    connections.insert(connection); connection->Start();
                    std::cout << "Clients=" << connections.size() << " activeMatches=" << match.ActiveCount() << std::endl;
                }
            }
            if (acceptor.is_open()) accept();
        });
    };
    net::steady_timer maintenance(io);
    std::function<void()> maintain;
    maintain = [&] {
        maintenance.expires_after(std::chrono::seconds(1));
        maintenance.async_wait([&](boost::system::error_code error) {
            if(error) return;
            match.Cleanup(); maintain();
        });
    };
    maintain();
    net::signal_set signals(io, SIGINT, SIGTERM);
    signals.async_wait([&](boost::system::error_code error, int) {
        if (error) return;
        boost::system::error_code ignored; acceptor.close(ignored);
        maintenance.cancel(); match.Shutdown();
        auto copy = connections;
        for (auto const& connection : copy) connection->Stop();
    });
    accept();
    std::cout << "Project C WebSocket listening on 0.0.0.0:" << acceptor.local_endpoint().port() << std::endl;
    io.run();
}
}
