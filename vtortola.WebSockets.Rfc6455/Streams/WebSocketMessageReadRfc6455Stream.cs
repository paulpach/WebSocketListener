﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace vtortola.WebSockets.Rfc6455
{
    public class WebSocketMessageReadRfc6455Stream : WebSocketMessageReadStream
    {
        private readonly WebSocketRfc6455 _webSocket;
        private bool _hasPendingFrames;
        private readonly WebSocketMessageType _messageType;
        private readonly WebSocketExtensionFlags _flags;

        public override WebSocketMessageType MessageType => _messageType;
        public override WebSocketExtensionFlags Flags => _flags;

        public WebSocketMessageReadRfc6455Stream(WebSocketRfc6455 webSocket)
        {
            Guard.ParameterCannotBeNull(webSocket, nameof(webSocket));

            _webSocket = webSocket;
            _messageType = (WebSocketMessageType)_webSocket.Connection.CurrentHeader.Flags.Option;
            _flags = GetExtensionFlags(_webSocket.Connection.CurrentHeader.Flags);
            _hasPendingFrames = !_webSocket.Connection.CurrentHeader.Flags.FIN;
            if (_webSocket.Connection.CurrentHeader.Flags.Option != WebSocketFrameOption.Binary && _webSocket.Connection.CurrentHeader.Flags.Option != WebSocketFrameOption.Text)
                throw new WebSocketException($"WebSocketMessageReadNetworkStream can only start with a Text or Binary frame, not {_webSocket.Connection.CurrentHeader.Flags.Option}." );
        }

        private WebSocketExtensionFlags GetExtensionFlags(WebSocketFrameHeaderFlags webSocketFrameHeaderFlags)
        {
            var flags = new WebSocketExtensionFlags();
            flags.Rsv1 = webSocketFrameHeaderFlags.RSV1;
            flags.Rsv2 = webSocketFrameHeaderFlags.RSV2;
            flags.Rsv3 = webSocketFrameHeaderFlags.RSV3;
            return flags;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

            if (count == 0)
                return 0;

            var read = 0;
            do
            {
                if (!_webSocket.IsConnected)
                    break;

                var bytesToRead = this.GetBytesToRead(this._webSocket.Connection.CurrentHeader, count);
                if (bytesToRead == 0 && !_hasPendingFrames)
                {
                    _webSocket.Connection.DisposeCurrentHeaderIfFinished();
                    break;
                }
                else if (bytesToRead == 0 && _hasPendingFrames)
                {
                    LoadNewHeader();
                }
                else
                {
                    read = _webSocket.Connection.ReadInternal(buffer, offset, bytesToRead);

                    offset += read;
                    count -= read;

                    _webSocket.Connection.DisposeCurrentHeaderIfFinished();

                    if (_webSocket.Connection.CurrentHeader == null && _hasPendingFrames)
                        LoadNewHeader();
                }
            } while (read == 0 && _webSocket.Connection.CurrentHeader.RemainingBytes != 0 && count > 0);

            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if(buffer == null) throw new ArgumentNullException(nameof(buffer));
            if(offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if(count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

            if (count == 0)
                return 0;

            var read = 0;
            do
            {
                if (!_webSocket.IsConnected || cancellationToken.IsCancellationRequested)
                    break;

                var bytesToRead = this.GetBytesToRead(this._webSocket.Connection.CurrentHeader, count);
                if (bytesToRead == 0 && !_hasPendingFrames)
                {
                    _webSocket.Connection.DisposeCurrentHeaderIfFinished();
                    break;
                }
                else if (bytesToRead == 0 && _hasPendingFrames)
                {
                    await LoadNewHeaderAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    read = await _webSocket.Connection.ReadInternalAsync(buffer, offset, bytesToRead, cancellationToken).ConfigureAwait(false);

                    offset += read;
                    count -= read;

                    _webSocket.Connection.DisposeCurrentHeaderIfFinished();

                    if (_webSocket.Connection.CurrentHeader == null && _hasPendingFrames)
                        await LoadNewHeaderAsync(cancellationToken).ConfigureAwait(false);
                }
            } while (read == 0 && _webSocket.Connection.CurrentHeader.RemainingBytes != 0 && count > 0);

            return read;
        }

        private int GetBytesToRead(WebSocketFrameHeader header, int bufferSize)
        {
            if (bufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(bufferSize));

            if (header == null)
                return 0;

            return (int)Math.Min(bufferSize, header.RemainingBytes);
        }


        private void LoadNewHeader()
        {
            _webSocket.Connection.AwaitHeader();
            _hasPendingFrames = _webSocket.Connection.CurrentHeader != null && !_webSocket.Connection.CurrentHeader.Flags.FIN && _webSocket.Connection.CurrentHeader.Flags.Option == WebSocketFrameOption.Continuation;
        }
        private async Task LoadNewHeaderAsync(CancellationToken cancellationToken)
        {
            await _webSocket.Connection.AwaitHeaderAsync(cancellationToken).ConfigureAwait(false);
            _hasPendingFrames = _webSocket.Connection.CurrentHeader != null && !_webSocket.Connection.CurrentHeader.Flags.FIN && _webSocket.Connection.CurrentHeader.Flags.Option == WebSocketFrameOption.Continuation;
        }
    }

}
