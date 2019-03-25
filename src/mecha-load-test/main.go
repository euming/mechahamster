package main

import (
	"context"
	"io"
	"log"
	"os"
	"os/signal"
	"strconv"
	"sync"
	"time"

	"mecha-load-test/internal/pb"

	"github.com/google/uuid"
	"go.uber.org/zap"
	"google.golang.org/grpc"
)

const (
	LoadPlayerRootID         = "demov1-"
	ServerMaxPlayers         = 4
	ServerTimeoutDuration    = 30 * time.Second // This should match the timeout set in the game code
	OpenMatchFailsafeTimeout = 30 * time.Second // This is arbitrary, but should be long enough that a match can be made
	SimulatePeriod           = 3 * time.Minute
	RestPeriod               = 2 * time.Minute
	MaxConcurrentGroups      = 4
	FrontEndIP               = "35.236.24.200"
	FrontEndPort             = "50504"
)

func allocatePlayerID() string {
	return LoadPlayerRootID + uuid.New().String()
}

func waitForResults(stream pb.Frontend_GetUpdatesClient, logger *zap.Logger) pb.Player {
	for {
		a, err := stream.Recv()
		if err == io.EOF {
			logger.Info("stream ended in EOF", zap.String("function", "waitForResults"))
			break
		}
		if err != nil {
			logger.Error("problem reading stream", zap.String("function", "waitForResults"), zap.String("err", err.Error()))
			break
		}
		return *a
	}
	return pb.Player{}
}

func spawnPlayerRequests(ctx context.Context, req chan time.Duration, logger *zap.Logger) {
	var wg sync.WaitGroup

	id := allocatePlayerID()

	for i := 0; i < ServerMaxPlayers; i++ {
		wg.Add(1)
		go func(index int) {
			defer wg.Done()

			conn, err := grpc.Dial(FrontEndIP+":"+FrontEndPort, grpc.WithInsecure())

			if err != nil {
				log.Fatalf("Cannot connect to GRPC endpoint: %v", err)
			}
			defer conn.Close()

			client := pb.NewFrontendClient(conn)

			player := &pb.Player{
				Id:         id + "-" + strconv.Itoa(index), // Each spawn request shares the ID so reading the logs is easier
				Properties: "{\"mode\": {\"demo\": 1}",
			}

			created, err := client.CreatePlayer(ctx, player)
			if err != nil {
				logger.Error("problem creating request", zap.String("function", "CreatePlayer"), zap.String("id", player.Id), zap.String("err", err.Error()))
				return
			}
			logger.Info("request processed", zap.String("function", "CreatePlayer"), zap.String("id", player.Id), zap.Bool("result", created.Success))

			timeoutCtx, timeoutCancel := context.WithTimeout(ctx, OpenMatchFailsafeTimeout)
			defer timeoutCancel()

			stream, err := client.GetUpdates(timeoutCtx, player)
			if err != nil {
				logger.Error("problem updating request", zap.String("function", "GetUpdates"), zap.String("id", player.Id), zap.String("err", err.Error()))
				return
			}

			result := waitForResults(stream, logger)
			logger.Info("request processed", zap.String("function", "waitForResults"), zap.String("id", result.Id))

			destroyed, err := client.DeletePlayer(ctx, player)
			if err != nil {
				logger.Error("problem creating request", zap.String("function", "DeletePlayer"), zap.String("id", player.Id), zap.String("err", err.Error()))
				return
			}
			logger.Info("request processed", zap.String("function", "DeletePlayer"), zap.String("id", player.Id), zap.Bool("result", destroyed.Success))

			logger.Debug("waiting", zap.Duration("duration", ServerTimeoutDuration))

			// Sleep until done or context is cancelled
			select {
			case <-ctx.Done():
				return
			case <-time.After(ServerTimeoutDuration):
				break
			}
		}(i)
	}

	wg.Wait()
	req <- 10 * time.Second
}

func main() {
	logger, err := zap.NewProduction()

	if err != nil {
		log.Fatalf("Cannot initialize Zap production logger: %v", err)
	}
	defer logger.Sync()

	ctx, cancel := context.WithCancel(context.Background())

	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, os.Interrupt)

	defer func() {
		signal.Stop(sigChan)
		cancel()
	}()

	timeoutChan := make(chan time.Duration, MaxConcurrentGroups)

	for i := 0; i < MaxConcurrentGroups; i++ {
		timeoutChan <- 10 * time.Second
	}

	t1 := time.Now()

Shutdown:
	for {
		var delay time.Duration

		// Pull a delay from the timeout pool or wait for CTRL+C
		select {
		case delay = <-timeoutChan:
			break
		case <-sigChan:
			cancel()
			logger.Info("sigterm", zap.String("function", "spawnPlayerRequests"))
			break Shutdown
		}

		go spawnPlayerRequests(ctx, timeoutChan, logger)

		// Sleep until done or context is cancelled
		select {
		case <-time.After(delay):
			logger.Info("request processed", zap.String("function", "spawnPlayerRequests"), zap.Duration("delay", delay))
			break
		case <-sigChan:
			cancel()
			logger.Info("sigterm", zap.String("function", "spawnPlayerRequests"))
			break Shutdown
		case <-ctx.Done():
			break Shutdown
		}

		// Simulate requests for some time then sleep for some time to show load and not-load situations
		t2 := time.Now()
		delta := t2.Sub(t1)

		if delta >= SimulatePeriod {
			logger.Info("wait", zap.Duration("RestPeriod", RestPeriod))
			select {
			case <-time.After(RestPeriod):
				t1 = time.Now()
				break
			case <-sigChan:
				cancel()
				logger.Info("sigterm", zap.String("function", "spawnPlayerRequests"))
				break Shutdown
			}
		} else {
			logger.Info("simulation remaining", zap.Duration("time", SimulatePeriod-delta))
		}
	}

	time.Sleep(1 * time.Second)
}
